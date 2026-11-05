using System.Globalization;
using System.Text;

namespace Lumina_Reserve_2.Services
{
    public class ImagenService
    {
        public const string AvatarPorDefecto = "/img/default-user.svg";
        public const string CortePorDefecto = "/img/default-corte.svg";
        private const long MaxBytes = 2 * 1024 * 1024;
        private static readonly string[] Extensiones = { ".jpg", ".jpeg", ".png", ".webp" };

        private static readonly HashSet<string> Excluidos = new(StringComparer.OrdinalIgnoreCase)
        {
            "diego", "erick", "fernando", "irvin", "ivan", "default-user", "default-corte"
        };

        private readonly string _imgDir;
        private readonly string _perfilesDir;
        private readonly ILogger<ImagenService> _log;

        public ImagenService(IWebHostEnvironment env, ILogger<ImagenService> log)
        {
            _log = log;
            var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
            _imgDir = Path.Combine(webRoot, "img");
            _perfilesDir = Path.Combine(_imgDir, "perfiles");
            Directory.CreateDirectory(_perfilesDir);
        }

        public string UrlPerfil(int usuarioId)
        {
            foreach (var ext in Extensiones)
            {
                var ruta = Path.Combine(_perfilesDir, usuarioId + ext);
                if (File.Exists(ruta))
                    return $"/img/perfiles/{usuarioId}{ext}?v={File.GetLastWriteTimeUtc(ruta).Ticks}";
            }
            return AvatarPorDefecto;
        }

        public async Task<(bool Ok, string? Error)> GuardarPerfilAsync(int usuarioId, IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
                return (false, "No se recibió ningún archivo.");
            if (archivo.Length > MaxBytes)
                return (false, "La imagen no puede pesar más de 2 MB.");

            var cabecera = new byte[12];
            await using (var lectura = archivo.OpenReadStream())
            {
                var leidos = await lectura.ReadAsync(cabecera.AsMemory(0, cabecera.Length));
                if (leidos < 12) return (false, "El archivo no es una imagen válida.");
            }

            var ext = DetectarExtension(cabecera);
            if (ext == null)
                return (false, "Formato no permitido. Usa JPG, PNG o WEBP.");

            var destino = Path.Combine(_perfilesDir, usuarioId + ext);
            var temporal = destino + ".tmp";
            try
            {
                await using (var salida = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await archivo.CopyToAsync(salida);
                }

                foreach (var otra in Extensiones)
                {
                    var vieja = Path.Combine(_perfilesDir, usuarioId + otra);
                    if (File.Exists(vieja)) File.Delete(vieja);
                }
                File.Move(temporal, destino);
                return (true, null);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "No se pudo guardar la foto de perfil del usuario {Id}", usuarioId);
                if (File.Exists(temporal)) File.Delete(temporal);
                return (false, "No se pudo guardar la imagen. Intenta de nuevo.");
            }
        }

        private static string? DetectarExtension(byte[] h)
        {
            if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
            if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
            if (h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' &&
                h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
            return null;
        }

        public string? UrlCorte(string? nombreCorte)
        {
            var buscado = Normalizar(nombreCorte);
            if (buscado.Length == 0 || !Directory.Exists(_imgDir)) return null;

            string? mejor = null;
            var mejorPuntaje = int.MaxValue;

            foreach (var ruta in Directory.EnumerateFiles(_imgDir))
            {
                var ext = Path.GetExtension(ruta);
                if (!Extensiones.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;

                var nombreArchivo = Path.GetFileNameWithoutExtension(ruta);
                if (Excluidos.Contains(nombreArchivo)) continue;

                var archivo = Normalizar(nombreArchivo);
                int puntaje;
                if (archivo == buscado) puntaje = 0;
                else if (archivo.StartsWith(buscado)) puntaje = 1 + archivo.Length - buscado.Length;
                else if (archivo.Contains(buscado)) puntaje = 100 + archivo.Length - buscado.Length;
                else continue;

                if (puntaje < mejorPuntaje)
                {
                    mejorPuntaje = puntaje;
                    mejor = "/img/" + Path.GetFileName(ruta);
                }
            }
            return mejor;
        }

        public string UrlCorteOPorDefecto(string? nombreCorte) => UrlCorte(nombreCorte) ?? CortePorDefecto;


        private static string Normalizar(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var d = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            var s = sb.ToString();
            if (s.StartsWith("el") && s.Length > 4) s = s[2..];
            return s;
        }
    }
}