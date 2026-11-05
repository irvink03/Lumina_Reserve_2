using System.Security.Cryptography;
using System.Text;
using Lumina_Reserve_2.Models;
using Microsoft.AspNetCore.Identity;

namespace Lumina_Reserve_2.Services
{
    public class PasswordService
    {
        private readonly PasswordHasher<Usuario> _hasher = new();

        public string Hash(Usuario usuario, string password) => _hasher.HashPassword(usuario, password);

        public (bool EsValida, bool DebeRehashear) Verificar(Usuario usuario, string password)
        {
            var guardado = usuario.PasswordHash;
            if (string.IsNullOrEmpty(guardado) || string.IsNullOrEmpty(password))
                return (false, false);

            if (PareceHashIdentity(guardado))
            {
                try
                {
                    var r = _hasher.VerifyHashedPassword(usuario, guardado, password);
                    return r switch
                    {
                        PasswordVerificationResult.Success => (true, false),
                        PasswordVerificationResult.SuccessRehashNeeded => (true, true),
                        _ => (false, false)
                    };
                }
                catch (FormatException) { }
            }

            // Formato antiguo: texto plano
            var igual = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(guardado),
                Encoding.UTF8.GetBytes(password));
            return (igual, igual);
        }

        private static bool PareceHashIdentity(string valor)
        {
            try
            {
                var bytes = Convert.FromBase64String(valor);
                return bytes.Length > 1 && (bytes[0] == 0x00 || bytes[0] == 0x01);
            }
            catch (FormatException) { return false; }
        }
    }
}