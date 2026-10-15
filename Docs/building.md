# ローカルビルド手順

CodexからはUnity EditorのCLIを優先して実行します。Windowsでの標準対象は
Windows x64です。既存のビルドメソッドを呼び出すため、EditorメニューとCLIで
同じビルド処理を使用できます。リリースの版数決定、署名、配布は
[releasing.md](releasing.md)を参照してください。

## 前提条件

1. `ProjectSettings/ProjectVersion.txt`と同じ版のUnityをUnity Hubでインストールします。
   2026年10月15日の作業ツリーでは`6000.6.5f1`です。
2. Unity Hubでライセンスを有効にし、対象プラットフォームのビルドモジュールを用意します。
3. 同じプロジェクトを開いているUnity Editorを終了します。未保存の編集は先に保存します。
4. `Assets/Main.unity`が存在し、Build Settingsのシーン一覧で有効であることを確認します。
   現在の設定には登録済みです。

初回のパッケージ取得にはネットワーク接続が必要になる場合があります。
Codexで実行する際も、Unity本体・ライセンス・モジュールはローカル環境のものを使います。

## CLIによるWindows x64ビルド

リポジトリのルートを作業ディレクトリとして、PowerShellで次を実行します。
Editorの配置を変更している場合は`$unity`を実際のパスに置き換えてください。

```powershell
$ErrorActionPreference = 'Stop'
$project = (Get-Location).Path
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
$version = (Select-String -Path $versionFile -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
$unity = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
if (-not (Test-Path -LiteralPath $unity)) {
    throw "Unity Editorが見つかりません: $unity"
}
$logDir = Join-Path $project 'Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ("build-windows-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$unityArgs = @(
    '-batchmode', '-quit',
    '-projectPath', ('"{0}"' -f $project),
    '-buildTarget', 'StandaloneWindows64',
    '-executeMethod', 'Jacquard.Editor.BuildDesktop.BuildWindows',
    '-logFile', ('"{0}"' -f $log)
)
$process = Start-Process -FilePath $unity -ArgumentList $unityArgs `
    -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "Unityビルド失敗: exit=$($process.ExitCode), log=$log"
}
if (-not (Select-String -LiteralPath $log -Pattern 'BuildDesktop: Succeeded' -Quiet)) {
    throw "成功ログを確認できません: $log"
}
if (-not (Test-Path -LiteralPath 'Build/Windows/Jacquard.exe')) {
    throw '成果物が見つかりません。ログを確認してください。'
}
Write-Output "ビルド成功: Build/Windows/Jacquard.exe; log=$log"
```

WindowsのGUI実行ファイルを確実に待機するため、`Start-Process -Wait`を使用します。
`-buildTarget`は起動時に指定してください。ターゲットごとにUnityを別プロセスで起動します。
これは対象に応じたコンパイルをビルドメソッド実行前に済ませるためです。
詳細は[Unity公式CLIビルド手順](https://docs.unity.com/en-us/engine/6000.7/manual/building-and-publishing/build-command-line)を参照してください。

成功時の出力は`Build/Windows/`です。配布・移動する場合は、exeだけでなく、
同じディレクトリ内のデータとランタイムも一緒に扱ってください。

## Unity EditorでのWindowsビルド

1. Unity Hubから、指定版のEditorでプロジェクトを開きます。
2. パッケージの取得とスクリプトのコンパイル完了を待ち、Consoleのエラーを解消します。
3. **File > Build Profiles**でWindowsを選択し、必要に応じて
   **Switch Platform**で切り替えます。
4. ビルド対象のシーン一覧で`Assets/Main.unity`を有効にします。
   カスタムBuild Profileを使う場合も、既存メソッドは
   `EditorBuildSettings.scenes`を参照することに注意してください。
5. **Jacquard > Build Windows**を実行します。
6. Consoleの`BuildDesktop: Succeeded`と`Build/Windows/Jacquard.exe`を確認します。
7. 出力したアプリを起動し、画面表示、サンプルスコアの読込、再生・停止、音声出力を確認します。

シーンが欠落している場合、またはシーン生成コードの変更を反映する場合に限り、
**Jacquard > Rebuild Main Scene**を実行します。これはシーンを再生成して保存し、
ビルド対象シーン一覧も書き換えるため、手作業のシーン編集は事前に確認してください。
CLIの対応メソッドは`Jacquard.Editor.SceneBuilder.Build`です。
実行する場合は、ビルドとは別のUnity起動で`-executeMethod`に指定します。

## 他プラットフォームの入口

次のCLI引数はWindows手順の`-buildTarget`と`-executeMethod`を置き換えて使います。
ホストOSに合わせてUnity実行ファイルのパスと起動方法も変更してください。

| 対象 | buildTarget | executeMethod | 出力 | 追加条件 |
| --- | --- | --- | --- | --- |
| Windows x64 | `StandaloneWindows64` | `Jacquard.Editor.BuildDesktop.BuildWindows` | `Build/Windows/Jacquard.exe` | Monoでビルド |
| macOS arm64 | `StandaloneOSX` | `Jacquard.Editor.BuildDesktop.BuildMac` | `Build/macOS/Jacquard.app` | macOS上のEditor、IL2CPP用ツールチェーン |
| Android APK | `Android` | `Jacquard.Editor.BuildAndroid.BuildApk` | `Build/Android/Jacquard.apk` | Android Build Support、SDK、NDK、JDK |
| Android AAB | `Android` | `Jacquard.Editor.BuildAndroid.BuildBundle` | `Build/Android/Jacquard.aab` | Android環境とアップロード鍵の環境変数 |
| iOS | `iOS` | `Jacquard.Editor.BuildIos.Build` | `Xcode/` | iOS Build Support、最終ビルドと署名はmacOSとXcode |

Editorではそれぞれ**Jacquard > Build macOS / Build Android /
Build Android Bundle / Build iOS**を使います。先に対象プラットフォームへ切り替えます。
AABの署名環境変数とiOSの後処理の確認項目は[releasing.md](releasing.md)にあります。
Webには専用のCLIビルドメソッドがないため、この標準手順の対象には含めません。
Web固有の実装上の注意は[impl-web.md](impl-web.md)にあります。

## 失敗時の確認と実行状況

失敗時は指定した`Logs/build-windows-*.log`を開き、最初のコンパイルエラー、
ライセンスエラー、モジュール不足、プロジェクトの二重起動を確認します。
以前の成果物が残る場合があるため、exeの存在だけでは成功と判定しないでください。
成功判定には今回のプロセス終了コードと今回の成功ログを使います。
ビルド後は`git status --short`でUnityが更新した設定やロックファイルも確認します。

2026年10月15日の環境調査では、指定版Editor、Windows用サポート、WebGL用サポート、
Mainシーンとその登録を確認しました。Android用サポートは見つかりませんでした。
本手順の策定時点ではビルドを実行しておらず、コンパイル成功、ライセンスの利用可否、
生成アプリの動作は未検証です。
