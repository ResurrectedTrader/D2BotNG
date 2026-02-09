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
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" size="sm" onClick={handleBack}>
            <ArrowLeftIcon className="h-4 w-4" />
          </Button>
          <div>
            <div className="flex items-center gap-3">
              <h1 className="text-2xl font-bold text-zinc-100">
                {isNewProfile ? "New Profile" : profile?.name}
              </h1>
              {!isNewProfile && status && (
                <ProfileStatusBadge
                  state={status.state}
                  status={status.status}
                />
              )}
            </div>
            {!isNewProfile && (
              <p className="mt-1 text-sm text-zinc-400">
                Configure your bot profile settings
              </p>
            )}
          </div>
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
