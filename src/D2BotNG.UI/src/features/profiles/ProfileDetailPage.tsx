/**
 * ProfileDetailPage component
 *
 * Detail view for a single profile.
 * Supports both creating new profiles and editing existing ones.
 */

import { useCallback, useMemo } from "react";
import { useParams, useNavigate, useSearchParams } from "react-router-dom";
import { Button, EmptyState } from "@/components/ui";
import { useCreateProfile, useUpdateProfile } from "@/hooks";
import { useProfile, useProfileState } from "@/stores/event-store";
import type { ProfileInput } from "@/hooks/useProfiles";
import { ProfileForm } from "./ProfileForm";
import { ProfileStatusBadge } from "./ProfileStatusBadge";
import { ArrowLeftIcon } from "@heroicons/react/24/outline";

export function ProfileDetailPage() {
  const { id: name } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const decodedName = name ? decodeURIComponent(name) : "";
  const navigate = useNavigate();
  const isNewProfile = !name;

  // Get clone source from query params (for cloning profiles)
  const cloneSource = searchParams.get("clone");

  // Get profile data from event store
  const profileData = useProfile(decodedName);
  const profile = profileData?.profile;
  const status = useProfileState(decodedName);

  // Get source profile for cloning
  const sourceProfileData = useProfile(cloneSource ?? "");
  const sourceProfile = sourceProfileData?.profile;

  // Build initial values for cloning (clear name and character)
  const cloneInitialValues = useMemo(() => {
    if (!sourceProfile) return undefined;
    return {
      ...sourceProfile,
      name: "",
      character: "",
    };
  }, [sourceProfile]);

  // Mutations
  const createProfile = useCreateProfile();
  const updateProfile = useUpdateProfile();

  const handleBack = useCallback(() => {
    navigate("/profiles");
  }, [navigate]);

  const handleSubmit = useCallback(
    async (data: ProfileInput) => {
      try {
        if (isNewProfile) {
          await createProfile.mutateAsync(data);
        } else {
          const isRename = decodedName !== data.name;
          await updateProfile.mutateAsync({
            profile: data,
            originalName: isRename ? decodedName : undefined,
          });
        }
        navigate("/profiles", { replace: true });
      } catch {
        // Error handling is done in the hooks
      }
    },
    [isNewProfile, createProfile, updateProfile, navigate, decodedName],
  );

  const handleCancel = useCallback(() => {
    navigate("/profiles");
  }, [navigate]);

  if (!isNewProfile && !profile) {
    return (
      <EmptyState
        title="Profile not found"
        description="The profile you're looking for doesn't exist or hasn't loaded yet."
        action={<Button onClick={handleBack}>Back to Profiles</Button>}
      />
    );
  }

  const isSubmitting = createProfile.isPending || updateProfile.isPending;

  return (
    <div className="space-y-4">
      {/* Sticky header */}
      <div className="sticky top-0 z-20 bg-zinc-950 -mx-4 px-4 sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 pt-4 pb-3 border-b border-zinc-800/50">
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="sm" onClick={handleBack}>
            <ArrowLeftIcon className="h-4 w-4" />
          </Button>
          <h1 className="text-lg font-bold text-zinc-100">
            {isNewProfile ? "New Profile" : profile?.name}
          </h1>
          {!isNewProfile && status && (
            <ProfileStatusBadge state={status.state} status={status.status} />
          )}
        </div>
      </div>

      {/* Profile Form */}
      <ProfileForm
        profile={profile}
        initialValues={cloneInitialValues}
        onSubmit={handleSubmit}
        onCancel={handleCancel}
        isLoading={isSubmitting}
      />
    </div>
  );
}
