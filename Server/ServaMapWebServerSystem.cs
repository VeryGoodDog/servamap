using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace ServaMap.Server;

public class ServaMapWebServerSystem : ModSystem {
	private ILogger logger;
	private HttpListener listener = new HttpListener();

	private PersistedConfiguration config => ServaMapServerMod.GetConfig(serverAPI);

	private string webmapPath => config.GetOrCreateSubDirectory(serverAPI, config.WebMapPath);

	private ICoreServerAPI serverAPI;

	public override double ExecuteOrder() => 0.3;

	public override void StartServerSide(ICoreServerAPI api) {
		serverAPI = api;
		logger = serverAPI.Logger;

		TransferWebMapAssets();

		listener.Prefixes.Add(config.WebServerUrlPrefix);
		listener.Start();

		Task.Run(() => {
			logger.Notification("Starting listener task!");
			while (listener.IsListening)
				listener.BeginGetContext(ListenerCallback, listener).AsyncWaitHandle.WaitOne();
		});

		serverAPI.Event.SaveGameLoaded += WriteWorldOriginInfo;

		serverAPI.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => listener.Stop());
	}

	private void TransferWebMapAssets() {
		var assets = serverAPI.Assets.GetMany("config", "servamap");

		foreach (var asset in assets) {
			var assetPath = asset.Location.Path;
			var assetData = asset.Data;
			if (assetData is null) {
				logger.Error($"ServaMap failed to load ${asset.Name}! The webmap will probably break.");
				continue;
			}
			var combinedPath = Path.Combine(webmapPath, assetPath);
			#if !DEBUG
			if (File.Exists(combinedPath))
				continue;
			#endif
			var dir = Path.GetDirectoryName(combinedPath);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir!);
			File.WriteAllBytes(combinedPath, assetData);
		}
	}

	private void ListenerCallback(IAsyncResult result) {
		var lis = (HttpListener)result.AsyncState;
		var context = lis.EndGetContext(result);
		var request = context.Request;
		var response = context.Response;

		var loc = request.Url;
		var localLoc = webmapPath + loc.LocalPath;

		byte[] buffer;
		if (File.Exists(localLoc)) {
			buffer = File.ReadAllBytes(localLoc);
			response.StatusCode = 200;
		}
		else {
			buffer = Encoding.UTF8.GetBytes($"<html><body>file not found</body></html>");
			response.StatusCode = 404;
		}

		response.ContentLength64 = buffer.Length;
		response.OutputStream.Write(buffer, 0, buffer.Length);
		response.OutputStream.Close();
	}

	private void WriteWorldOriginInfo() {
		var serverMain = serverAPI.World as ServerMain;
		var mm = serverMain.MapSize / 2;
		var dsp = serverMain.DefaultSpawnPosition.XYZInt;
		var worldOriginOffset = new Vec3i(dsp.X - mm.X, dsp.Y - mm.Y, dsp.Z - mm.Z);

		var infoPath = Path.Combine(config.GetOrCreateWebMapSubDirectory(serverAPI, config.MapApiDataPath),
				"worldOriginOffset.json");
		using var stream = new StreamWriter(infoPath, false, Encoding.UTF8);
		using var writer = new JsonTextWriter(stream);
		writer.Formatting = Formatting.None;
		writer.WriteObject(() =>
				writer.WriteArray("worldOriginOffset", worldOriginOffset.X, worldOriginOffset.Y, worldOriginOffset.Z));
	}
}