using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mobishare.Core.Models.Vehicles;
using Microsoft.AspNetCore.SignalR;
using Mobishare.Infrastructure.Services.SignalR;
using Mobishare.Core.VehicleStatus;
using System.Net.Http.Json;

namespace Mobishare.Infrastructure.Services.MQTT;

public class MqttMessageHandler : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MqttMessageHandler> _logger;
    private readonly MqttReceiver _receiver;
    private readonly IHubContext<VehicleHub> _hubContext;

    public MqttMessageHandler(
        IHttpClientFactory httpClientFactory,
        ILogger<MqttMessageHandler> logger,
        IHubContext<VehicleHub> hubContext)
    {
        _httpClient = httpClientFactory.CreateClient("CityApi");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));

        _receiver = new MqttReceiver("mosquitto", "arduino/gps");
        _receiver.TopicMessage += OnMessageReceived;
    }

    public async Task StartAsync() => await _receiver.StartAsync();
    public async Task StopAsync() => await _receiver.StopAsync();

    public void Dispose()
    {
        _receiver.TopicMessage -= OnMessageReceived;
        _receiver.Dispose();
    }

    private async void OnMessageReceived(string payload)
    {
        try
        {
            // Esempio: il payload è un JSON del tipo { "latitude": ..., "longitude": ..., "vehicleId": ... }
            var vehiclePosition = JsonSerializer.Deserialize<Position>(payload, options: new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (vehiclePosition is null)
            {
                _logger.LogWarning("Failed to deserialize payload: {Payload}", payload);
                return;
            }

            if (vehiclePosition.Latitude == 0 && vehiclePosition.Longitude == 0)
            {
                _logger.LogWarning("Failed to retrieve position: lat: {Latitude}, lon {Longitude}", vehiclePosition.Latitude, vehiclePosition.Longitude);
                return;
            }

            var responseVehicle = await _httpClient.GetFromJsonAsync<Vehicle>($"api/Vehicle/{vehiclePosition.VehicleId}");
            var vehicle = responseVehicle;

            if(vehicle == null)
            {
                _logger.LogWarning("Vehicle {VehicleId} does not exist.", vehiclePosition.VehicleId);
                return;
            }

            // Solo aggiorna la posizione se il veicolo è libero, ma invia sempre l'aggiornamento al frontend
            if (vehicle.Status == VehicleStatusType.Free.ToString())
            {
                var createResponse = await _httpClient.PostAsJsonAsync("api/Position", vehiclePosition);

                if (!createResponse.IsSuccessStatusCode)
                {
                    var errorContent = await createResponse.Content.ReadAsStringAsync();
                    _logger.LogError($"API error: {createResponse.StatusCode}, Content: {errorContent}");
                }
            }
            
            // Invia sempre l'aggiornamento al frontend con lo status del veicolo
            var vehicleUpdate = new 
            {
                VehicleId = vehiclePosition.VehicleId,
                Latitude = vehiclePosition.Latitude,
                Longitude = vehiclePosition.Longitude,
                Status = vehicle.Status,
                BatteryLevel = vehicle.BatteryLevel
            };
            
            await _hubContext.Clients.All.SendAsync("ReceiveVehiclePositionUpdate", vehicleUpdate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message: {Payload}", payload);
        }
    }
}
