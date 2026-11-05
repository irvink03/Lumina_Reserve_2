using Lumina_Reserve_2.Models;
using Microsoft.EntityFrameworkCore;

namespace Lumina_Reserve_2.Services
{
    public class RecordatorioCitasService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<RecordatorioCitasService> _log;

        public RecordatorioCitasService(IServiceScopeFactory scopeFactory, IConfiguration config,
                                        ILogger<RecordatorioCitasService> log)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var minutosAntes = Math.Clamp(_config.GetValue("Recordatorios:MinutosAntes", 60), 5, 24 * 60);
            var intervalo = Math.Clamp(_config.GetValue("Recordatorios:IntervaloMinutos", 1), 1, 30);
            _log.LogInformation("Recordatorios activos: {Min} min antes, revisando cada {Int} min.", minutosAntes, intervalo);

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await EnviarRecordatoriosAsync(minutosAntes, stoppingToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { _log.LogError(ex, "Error en el ciclo de recordatorios."); }

                try { await Task.Delay(TimeSpan.FromMinutes(intervalo), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task EnviarRecordatoriosAsync(int minutosAntes, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var telegram = scope.ServiceProvider.GetRequiredService<TelegramService>();

            var ahora = Reloj.Ahora;
            var limite = ahora.AddMinutes(minutosAntes);

            var citas = await context.Citas
                .Include(c => c.Usuario)
                .Include(c => c.Sucursal)
                .Where(c => (c.Estado == "Pendiente" || c.Estado == "Confirmada")
                         && !c.RecordatorioEnviado
                         && c.FechaHora > ahora
                         && c.FechaHora <= limite)
                .ToListAsync(ct);

            foreach (var cita in citas)
            {
                var chat = cita.Usuario?.TelegramChatId;
                if (chat.HasValue && chat.Value != 0)
                {
                    var faltan = Math.Max(1, (int)Math.Round((cita.FechaHora - ahora).TotalMinutes));
                    var mensaje =
                        $"⏰ *Lumina Reserve - Recordatorio*\n\n" +
                        $"¡Hola {cita.Usuario!.Nombre}! Tu cita es en aproximadamente {faltan} min.\n" +
                        $"🕒 *Hora:* {cita.FechaHora:HH:mm}\n" +
                        (cita.Sucursal != null ? $"📍 *Sucursal:* {cita.Sucursal.Nombre}\n" : "") +
                        "\nTe recomendamos llegar 10 minutos antes. ¡Te esperamos!";

                    var enviado = await telegram.EnviarMensajeAsync(chat.Value.ToString(), mensaje);
                    if (!enviado) continue;
                }
                cita.RecordatorioEnviado = true;
            }

            if (citas.Any(c => c.RecordatorioEnviado))
                await context.SaveChangesAsync(ct);
        }
    }
}