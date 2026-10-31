using System.Net;
using Nokto.Core.Services;

if (args.Contains("--shazam-service-smoke")) { await KeylessRecognitionProbe.SmokeAsync(); return; }
if (args.Contains("--lyrics-service-smoke")) { await LyricsLookupProbe.SmokeAsync(); return; }
if (args.Contains("--lyrics-lookup-probe")) { await LyricsLookupProbe.Run(); return; }
await KeylessRecognitionProbe.Run();
await MusicServiceProbe.Run();
await LyricsLookupProbe.Run();

static void Assert(bool ok,string message)
{
    if(!ok) throw new Exception("[FAIL] "+message);
    Console.WriteLine("[PASS] "+message);
}

var fake = new WeatherHandler();
using var client = new HttpClient(fake);
using var weather = new IslandWeatherService(client);
var now = new DateTimeOffset(2026,10,26,15,0,0,TimeSpan.Zero);
var first = await weather.GetAsync("Maipú, Chile",now);
Assert(first.City == "Maipú" && first.Country == "Chile", "Selección de país explícito sin ubicación del dispositivo");
Assert(first.TemperatureC == 21.5 && first.HumidityPercent == 50, "Temperatura y humedad");
Assert(first.Condition == "Despejado", "Código climático interpretado");
Assert(fake.Calls == 2, "Primera consulta usa geocodificación y clima");
var cached = await weather.GetAsync("Maipú, Chile",now.AddMinutes(15));
Assert(ReferenceEquals(first,cached) && fake.Calls == 2,"Caché de 30 min: cero peticiones repetidas");
await weather.GetAsync("Maipú, Chile",now.AddMinutes(31));
Assert(fake.Calls == 4,"Caducidad reactiva tras 30 min");
await weather.GetAsync("Maipú, Chile",now.AddMinutes(32),force:true);
Assert(fake.Calls == 6,"Actualización manual vence la caché");
bool invalid=false;
try{await weather.GetAsync("!",now);}
catch(ArgumentException){invalid=true;}
Assert(invalid && fake.Calls==6,"Ciudad inválida nunca inicia conexión");
Assert(IslandWeatherService.Describe(95)=="Tormenta","Clasificación de tormentas");
var local = await weather.GetAtLocationAsync(-33.52, -70.76, now);
Assert(fake.Calls == 8 && local.City == "Maipú" && local.Country == "Chile" && local.HumidityPercent == 50,
    "Ubicación muestra la localidad resuelta y el pronóstico");
Assert(local.Detail.Contains("© OpenStreetMap") && fake.ReverseCalls == 1, "Atribución de la localidad");
await weather.GetAtLocationAsync(-33.5201, -70.7601, now.AddMinutes(1));
Assert(fake.Calls == 9 && fake.ReverseCalls == 1, "Localidad aproximada en caché, sin repetir geocodificación");
invalid = false;
try { await weather.GetAtLocationAsync(double.NaN, 0, now); }
catch (ArgumentOutOfRangeException) { invalid = true; }
Assert(invalid && fake.Calls == 9, "Coordenadas inválidas no inician conexión");
var failedPlace = new WeatherHandler { FailReverse = true };
using (var fallbackClient = new HttpClient(failedPlace))
using (var fallbackWeather = new IslandWeatherService(fallbackClient))
{
    var fallback = await fallbackWeather.GetAtLocationAsync(-33.52, -70.76, now);
    Assert(fallback.TemperatureC == 21.5 && fallback.City.Contains("33.52° S") && !fallback.City.Contains("Ubicación actual"),
        "Fallo de localidad conserva el clima y muestra coordenadas aproximadas");
}
Console.WriteLine("[PASS] Comprobaciones de clima completadas");

sealed class WeatherHandler:HttpMessageHandler
{
    public int Calls;
    public int ReverseCalls;
    public bool FailReverse;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token)
    {
        Calls++;
        string content;
        if(req.RequestUri!.Host.StartsWith("geocoding-",StringComparison.Ordinal))
        {
            content = """{"results":[{"name":"Maipú","country":"Argentina","country_code":"AR","latitude":-32.96,"longitude":-68.78},{"name":"Maipú","country":"Chile","country_code":"CL","latitude":-33.52,"longitude":-70.76}]}""";
        }
        else if(req.RequestUri.Host == "nominatim.openstreetmap.org")
        {
            ReverseCalls++;
            if (!req.Headers.UserAgent.ToString().Contains("NoktoDev") || !req.RequestUri.Query.Contains("zoom=10"))
                throw new Exception("Consulta de localidad sin identificación o demasiado precisa.");
            if (FailReverse) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            content = """{"address":{"city":"Maipú","state":"Región Metropolitana","country":"Chile"}}""";
        }
        else if(req.RequestUri.Host.StartsWith("api.open-meteo",StringComparison.Ordinal))
        {
            if(!req.RequestUri.Query.Contains("latitude=-33.52",StringComparison.Ordinal))
                throw new Exception("Usó otra ciudad.");
            content = """{"current":{"temperature_2m":21.5,"relative_humidity_2m":50,"weather_code":0}}""";
        }
        else throw new Exception("Host no autorizado: "+req.RequestUri);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content=new StringContent(content) });
    }
}
