/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_TENANT_ID?: string
  readonly VITE_API_KEY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
