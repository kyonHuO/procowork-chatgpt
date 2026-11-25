# ProCowork for ArcGIS Pro with ChatGPT

ArcGIS Pro のチャット欄から、自然言語でレイヤーを調べたり、ArcPy で地理処理を実行したりできます。[ProCowork](https://github.com/mraad/ProCowork) をベースに、公式の [Sign in with ChatGPT Devkit](https://github.com/openai/sign-in-with-chatgpt-devkit) と [Codex app-server](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server) を接続した非商用版です。

## インストール

- Windows x64、ArcGIS Pro 3.5 系、Node.js 22.12 以降、Windows x64 版 Codex CLI が必要です。ArcGIS Pro 3.5.3 で動作確認しています。
- Codex CLI は、たとえば PowerShell で `npm install -g @openai/codex` と実行してインストールします。
- `ProCowork-ChatGPT-0.1.15.esriAddinX` をダブルクリックしてインストールし、ArcGIS Pro を再起動します。

## 使い方

1. ArcGIS Pro でプロジェクトを開き、**ProCowork → Chat → Continue with ChatGPT** を選びます。
2. ブラウザーで自分の ChatGPT アカウントを選び、プラン利用を許可します。利用できるモデルと上限はアカウントやワークスペースによって異なります。
3. チャット欄で「現在のマップのレイヤーを列挙して」などと送信します。**Show tool details** で生成コードとツールの結果を表示できます。

ArcPy を初めて実行したとき、ArcGIS Pro がアドイン同梱の Python ツールボックスの実行確認を表示することがあります。ArcGIS ツールは開いているプロジェクトで実行されます。書き込みを含む依頼を試すときは、バックアップや試験用プロジェクトを使ってください。

OAuth の認証情報は、Windows DPAPI を使って利用者の PC に暗号化して保存します。チャットのリクエストや必要なツール出力は、応答を生成するために OpenAI に送信されます。**Disconnect** で接続を解除できます。

## ソースからビルド

Git、Node.js 22.12 以降、.NET 8 SDK、Visual Studio 2022 または Visual Studio Build Tools の MSBuild を用意し、リポジトリのルートで実行します。

```powershell
.\build\Build-Release.ps1
```

成果物は `artifacts/release/ProCowork-ChatGPT-0.1.15.esriAddinX` に作られます。ビルド時に固定したバージョンの公式 Devkit を取得し、Codex app-server への OAuth 接続に必要な変更を加えます。配布用アドインには Devkit とその依存パッケージが含まれます。

## ライセンス

元の ProCowork の著作権表示と MIT ライセンスは [LICENSE](LICENSE) に記載しています。同梱する OpenAI Devkit と Devkit に対する変更には [Sign-in with ChatGPT Devkit Noncommercial License v1.0](build/Devkit-LICENSE.txt) が適用されます。Devkit の変更点と第三者ライセンスの同梱先は [Devkit-NOTICE.md](build/Devkit-NOTICE.md) に記載しています。この配布版は非商用利用向けです。
