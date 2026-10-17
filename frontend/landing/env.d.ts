/// <reference types="vite/client" />

/** Build-time site settings (vite.config.ts → src/lib/siteConfig.ts). */
declare const __SITE__: import('./src/lib/siteConfig').SiteConfig

declare module 'virtual:brand.css'
