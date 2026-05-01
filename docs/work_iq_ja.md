# Work IQ 連携

ContextRelay for Visual Studio は `/workiq` スラッシュコマンドを通じて Work IQ Gateway へ問い合わせできます。Work IQ は、メール、会議、ファイル、組織ナレッジなどの Microsoft 365 ワークインテリジェンスに AI でアクセスするための仕組みです。

## 前提条件

- **Microsoft 365 Copilot ライセンス** を持つユーザー
- **Tools > Options > ContextRelay > Authentication** で設定した ContextRelay 用 Entra アプリ登録
- 組織に **Work IQ サービス プリンシパル** がプロビジョニングされていること
- アプリ登録に **`WorkIQAgent.Ask`** 委任アクセス許可が追加され、管理者同意が付与されていること

## テナントで Work IQ を有効化する

### 1. Work IQ サービス プリンシパルをプロビジョニングする

Graph Explorer など、管理者権限で Microsoft Graph を呼び出せるクライアントから、Work IQ サービス プリンシパルを作成します。

```http
POST https://graph.microsoft.com/v1.0/servicePrincipals
Content-Type: application/json

{
  "appId": "fdcc1f02-fc51-4226-8753-f668596af7f7"
}
```

`201 Created` が返れば、テナント内で Work IQ リソースが有効になっています。競合エラーは既に存在することを示す場合があります。

### 2. アプリ登録に `WorkIQAgent.Ask` を追加する

1. **Microsoft Entra 管理センター** を開きます。
2. **アプリの登録** で ContextRelay のアプリを選択します。
3. **API のアクセス許可** > **アクセス許可の追加** > **自分の組織で使用している API** を開きます。
4. **Work IQ** を検索します。
5. 委任アクセス許可 **WorkIQAgent.Ask** を追加します。
6. テナント管理者の同意を付与します。

## `/workiq` の使い方

例:

```text
/workiq Alice からの最近のメールを要約して
/workiq 今日の会議を教えて
/workiq Q3 予算レビューに関する文書を探して
```

連続する `/workiq` は、直前の Work IQ `contextId` を引き継ぎます。会話状態をリセットするには `/clear` または **Clear Chat** を使ってください。

## プロトコル詳細

- エンドポイント: `https://workiq.svc.cloud.microsoft/a2a/`
- プロトコル: JSON-RPC 2.0 `SendMessage`
- 必須ヘッダー: `A2A-Version: 1.0`
- トークン audience: `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask`

ContextRelay は "today" や "this week" のような時間依存クエリを正しく解釈できるよう、各リクエストにローカルのタイムゾーン情報も付加します。

## デバッグ ログ

**Tools > Options > ContextRelay > General** で **Work IQ debug logging** を有効にすると、**ContextRelay Debug** ペインに構造メタデータを書き込みます。

プロンプト本文と応答本文は意図的にログへ残しません。

## トラブルシューティング

| 症状 | 対処 |
|---|---|
| `401 Unauthorized` | 再認証してください。Work IQ トークンの期限切れが考えられます。 |
| `403 Forbidden` | `WorkIQAgent.Ask` の管理者同意と、サインイン ユーザーの Microsoft 365 Copilot ライセンスを確認してください。 |
| 応答が空、または失敗する | 少し待って再試行してください。Work IQ が新規ライセンス ユーザーをまだインデックス中の可能性があります。 |
| `AADSTS65001` / `AADSTS65002` | Work IQ または Graph の管理者同意が不足しています。 |

## 参考情報

- [Work IQ API quickstart](https://learn.microsoft.com/ja-jp/microsoft-365/copilot/extensibility/work-iq-api-quickstart)
- [A2A protocol specification](https://a2a-protocol.org/latest/specification/)
