# Cuboid.CallingBot (Starter)

Minimal .NET 8 Web API scaffold for the Cuboid Teams calling service.

## What you need to add next
- Graph Calling (app-hosted media) join + media sockets
- ASR (wake phrase "cuboid") + TTS playback
- Brain client POST to /llm/respond

## Local run
```
dotnet build
dotnet run
open http://localhost:5000/healthz
```

## Deploy via GitHub Actions (Azure App Service)
1. Create an **Azure Web App** (Linux, .NET 8). Note its name.
2. In Azure Web App → **Overview** → Download **Publish profile**.
3. In GitHub repo → **Settings → Secrets → Actions**:
   - New secret: **AZURE_WEBAPP_PUBLISH_PROFILE** (paste entire XML).
4. Edit `.github/workflows/azure-webapp-dotnet.yml`:
   - Set `AZURE_WEBAPP_NAME` to your Web App name.
5. Push to `main`. GitHub Actions will build and deploy.
