namespace Lumina_Reserve_2.Services
{
    // Envía al administrador (Telegram:AdminChatId) un resumen diario a la hora configurada
    // en ResumenDiario:Hora (por defecto 08:00): citas de hoy e ingresos de ayer por sucursal.
    // El registro en Notificaciones (sp_ControlResumenDiario) evita repetirlo si la app se reinicia.
    public class ResumenDiarioService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<ResumenDiarioService> _log;

        public ResumenDiarioService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<ResumenDiarioService> log)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("ResumenDiario:Activo", true))
            {
                _log.LogInformation("Resumen diario por Telegram desactivado (ResumenDiario:Activo = false).");
                return;
            }
            if (!TelegramService.TokenUsable(_config["Telegram:BotToken"]) || string.IsNullOrWhiteSpace(_config["Telegram:AdminChatId"]))
            {
                _log.LogWarning("Resumen diario desactivado: falta Telegram:BotToken o Telegram:AdminChatId.");
                return;
            }

            var hora = TimeSpan.TryParse(_config["ResumenDiario:Hora"], out var h) && h >= TimeSpan.Zero && h < TimeSpan.FromDays(1)
                ? h : new TimeSpan(8, 0, 0);
            _log.LogInformation("Resumen diario por Telegram programado a las {Hora:hh\\:mm}.", hora);

            while (!stoppingToken.IsCancellationRequested)
            {
                var ahora = Reloj.Ahora;
                // Si la app arranca después de la hora, se envía en cuanto inicia (una sola vez por día).
                if (ahora.TimeOfDay >= hora)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var reportes = scope.ServiceProvider.GetRequiredService<ReportesAdminService>();
                        var resultado = await reportes.EnviarResumenDiarioAsync(ahora.Date, forzar: false, stoppingToken);
                        if (resultado == ReportesAdminService.ResultadoResumen.Enviado)
                            _log.LogInformation("Resumen diario del {Fecha:dd/MM/yyyy} enviado al administrador.", ahora);
                        else if (resultado == ReportesAdminService.ResultadoResumen.ErrorTelegram)
                        {
                            _log.LogWarning("No se pudo enviar el resumen diario; se reintentará en 15 minutos.");
                            await Esperar(TimeSpan.FromMinutes(15), stoppingToken);
                            continue;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Error al generar el resumen diario; se reintentará en 15 minutos.");
                        await Esperar(TimeSpan.FromMinutes(15), stoppingToken);
                        continue;
                    }
                }

                // Dormir hasta la próxima hora programada (hoy si aún no llega, si no mañana).
                var siguiente = ahora.Date.Add(hora);
                if (siguiente <= ahora) siguiente = siguiente.AddDays(1);
                await Esperar(siguiente - Reloj.Ahora, stoppingToken);
            }
        }

        private static async Task Esperar(TimeSpan tiempo, CancellationToken ct)
        {
            if (tiempo < TimeSpan.FromSeconds(1)) tiempo = TimeSpan.FromSeconds(1);
            try { await Task.Delay(tiempo, ct); } catch (OperationCanceledException) { }
        }
    }
}
