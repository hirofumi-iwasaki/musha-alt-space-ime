# musha-alt-space-ime

Windows 11 向けの小さな通知領域常駐アプリです。左 Alt + Space を、現在の入力先での手動 Alt + バッククォート相当のキー操作へ変換します。左 Alt 単独は無効にします。

## 状態

0.1.0 は試作版です。macOS 上でのビルドと自動テストは、Windows の入力フック、通知領域 UI、ATOK、Microsoft IME の実行時動作を証明しません。Windows 11 実機での検証記録がそろうまで、対応環境を保証しません。

20件の自動テストとWindows上のビルド・ZIP作成は成功しています。[動作確認用パッケージ](https://github.com/hirofumi-iwasaki/musha-alt-space-ime/actions/runs/35805415748/artifacts/10727413086)（GitHubへのログインが必要な場合があります）をダウンロードできます。Actions成果物のため保存期限があります。

特に ATOK とキーボード配列（US / JIS）の組み合わせは未検証です。配列に適したバッククォートのキー操作を決められない場合、アプリは Alt + Space を変換せず元の操作を維持します。別のキーへ黙って置き換えることはしません。

- [設計書](.chatgpt/DESIGN.md)
- [0.1.0 実装・検証記録](.chatgpt/IMPLEMENTATION_0.1.0.md)

## ビルドとテスト

開発には .NET SDK 10 が必要です。コアのテストは外部テストフレームワークに依存しないコンソールランナーです。

```powershell
dotnet run -c Release --project tests/MushaAltSpaceIme.Core.Tests/MushaAltSpaceIme.Core.Tests.csproj
```

Windows x64 の自己完結 ZIP を作るには、PowerShell で次を実行します。

```powershell
pwsh -File tool/build-package.ps1
```

生成先は `artifacts/musha-alt-space-ime-windows-x64.zip` です。ZIP の `app/` に `musha-alt-space-ime.exe` と同梱ランタイムを格納します。ルートには `README.md`、設計・検証記録、ビルド元を示す `SOURCE-INFO.txt` を格納します。同じ場所に ZIP の SHA-256 を記した `.sha256` ファイルも作成されます。

実行するには ZIP 全体を `%LocalAppData%\Programs\musha-alt-space-ime\` など固定の場所へ展開し、`app\musha-alt-space-ime.exe` を起動します。.NET の別途インストールは不要です。`app/` 内のファイルは一緒に置いてください。アプリ自身のメニューからログオン時起動を有効にできます。場所を移す場合は移動先で自動起動を登録し直してください。配布 ZIP はインストーラーではなく、アンインストール時は自動起動をメニューで無効にしてからアプリを終了し、展開先を削除してください。

## 操作

通知領域のアイコンを右クリックすると「ログオン時に起動」「一時停止／再開」「終了」を選べます。青いアイコンは有効、灰色は一時停止中です。アイコンが見えない場合はWindowsの通知領域の折りたたみ部分を確認してください。

- 左Altを押してからSpaceを押すと、Alt + バッククォート相当を1回送信します。Spaceの長押しでは繰り返しません。
- 左Alt単独は何もしません。Alt + Tab、Alt + F4などは通常操作へ引き継ぎます。
- 右Alt、Ctrl / Shift / Win併用、Spaceを先に押した場合は変換しません。
- 一時停止中は通常のWindows入力に戻ります。起動し直すと有効に戻ります。
- 通常権限のアプリが対象です。管理者として起動した入力先では、キー送信が制限されます。

## CI

`release/0.1.0` への push と pull request で、Windows ランナー上でテストと自己完結 x64 パッケージを実行します。成功時は `musha-alt-space-ime-windows-x64` という workflow artifact として ZIP をダウンロードできます。この workflow は Git タグや GitHub Release を作成・公開しません。
