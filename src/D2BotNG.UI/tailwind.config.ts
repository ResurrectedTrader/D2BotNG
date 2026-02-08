import type { Config } from 'tailwindcss'
import forms from '@tailwindcss/forms'

export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        d2: {
          gold: '#c9a227',
          'gold-light': '#e6c75a',
          'gold-dark': '#967819',
        },
        item: {
          normal: '#ffffff',
          magic: '#6969ff',
          rare: '#ffff00',
          set: '#00ff00',
          unique: '#c9a227',
          craft: '#ffa500',
        },
        state: {
          stopped: '#888888',
          starting: '#ffca28',
          running: '#4caf50',
          busy: '#ff9800',
          error: '#f44336',
        },
        zinc: {
          950: '#09090b',
          900: '#18181b',
          800: '#27272a',
          700: '#3f3f46',
          600: '#52525b',
          500: '#71717a',
          400: '#a1a1aa',
          300: '#d4d4d8',
          200: '#e4e4e7',
          100: '#f4f4f5',
          50: '#fafafa',
        },
      },
      fontFamily: {
        sans: [
          'Inter',
          'ui-sans-serif',
          'system-ui',
          '-apple-system',
          'sans-serif',
        ],
        exocet: ['Exocet', 'serif'],
      },
    },
  },
  plugins: [forms],
} satisfies Config
