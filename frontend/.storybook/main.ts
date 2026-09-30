import type { StorybookConfig } from '@storybook/react-vite';

// Component catalogue of the PolvorApp composites (ADR-0009 §4, design D8).
const config: StorybookConfig = {
  stories: ['../src/components/app/**/*.stories.tsx'],
  addons: ['@storybook/addon-a11y'],
  framework: { name: '@storybook/react-vite', options: {} },
  core: { disableTelemetry: true },
  viteFinal: (viteConfig) => ({
    ...viteConfig,
    // The PWA plugin is for the app build only.
    plugins: (viteConfig.plugins ?? [])
      .flat()
      .filter(
        (plugin) =>
          !(
            plugin &&
            typeof plugin === 'object' &&
            'name' in plugin &&
            plugin.name.startsWith('vite-plugin-pwa')
          ),
      ),
  }),
};

export default config;
