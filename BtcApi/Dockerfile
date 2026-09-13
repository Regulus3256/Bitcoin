# ---------------------------------------------------
# 1. ビルド用ステージ（SDK環境）
# ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# プロジェクトファイルをコピーして NuGet パッケージを復元
COPY *.csproj ./
RUN dotnet restore

# 全ソースコードをコピーしてリリースビルド
COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------------------------------------------------
# 2. 実行用ステージ（軽量なRuntime環境）
# ---------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# ビルド用ステージから成果物のみをコピー
COPY --from=build /app/publish .

# コンテナがListenするポートを設定
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# アプリケーションの実行（※ .csproj の名前に合わせて変更してください）
ENTRYPOINT ["dotnet", "BtcApi.dll"]