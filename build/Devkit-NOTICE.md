# Sign-in with ChatGPT Devkit

Upstream: https://github.com/openai/sign-in-with-chatgpt-devkit/tree/0a36fefeb913055c8c7a1a29b63d82b96b2e841a

Copyright 2026 OpenAI. The OpenAI-authored Devkit and ProCowork's changes to it are distributed under the Sign-in with ChatGPT Devkit Noncommercial License v1.0. The full license is included at `Install/Engine/sdk/dist/LICENSE`. The Devkit's third-party notices and dependency inventory are in the same folder. The included npm packages retain their own license files.

The release build modifies `packages/local/src/index.ts` and `packages/local/src/types.ts` to pass a refreshed OAuth access token to the official Codex app-server in a trusted local callback. Both files are marked by the release script. Source for these modifications is in `Build-Release.ps1`.
