-- =====================================================================
-- LUMINA RESERVE - RECUPERACIÓN DE CONTRASEÑA POR TELEGRAM
-- Ejecutar sobre LuminaReserveDB (CREATE OR ALTER: se puede re-ejecutar).
-- Desde sqlcmd usar -f 65001 para que los acentos se guarden bien.
--
-- Los códigos se guardan en CodigosVerificacionBot con prefijo 'R'
-- (ej. 'R482913') para no mezclarse con los códigos de inicio de sesión.
-- El código de 6 dígitos y el hash de la contraseña se generan en la aplicación.
-- ---------------------------------------------------------------------
--   @Opcion 1: Solicitar código. Devuelve Enviar = 1 y el TelegramChatId si
--              la cuenta existe, está activa y tiene Telegram vinculado.
--              Motivo: 'OK' | 'SinCuenta' | 'SinTelegram' | 'SinVerificar' | 'Reciente'
--   @Opcion 2: Restablecer. Valida el código y reemplaza PasswordHash
--              en una transacción; el código queda marcado como usado.
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.sp_RecuperarPassword
    @Opcion INT,
    @Correo VARCHAR(100),
    @Codigo VARCHAR(9) = NULL,
    @PasswordHash VARCHAR(256) = NULL,
    @MinutosVigencia INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SET @Correo = LOWER(LTRIM(RTRIM(ISNULL(@Correo, ''))));

    DECLARE @Usr INT, @Chat BIGINT, @Nombre VARCHAR(100);
    SELECT @Usr = UsuarioID, @Chat = TelegramChatId, @Nombre = Nombre
    FROM dbo.Usuarios
    WHERE LOWER(Correo) = @Correo AND ISNULL(Estado, 1) = 1;

    IF @Opcion = 1
    BEGIN
        IF @Usr IS NULL
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, CAST(NULL AS BIGINT) AS TelegramChatId, CAST(NULL AS VARCHAR(100)) AS Nombre, 'SinCuenta' AS Motivo;
            RETURN;
        END
        IF @Chat IS NULL OR @Chat = 0
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, CAST(NULL AS BIGINT) AS TelegramChatId, @Nombre AS Nombre, 'SinTelegram' AS Motivo;
            RETURN;
        END
        -- Seguridad: solo se envía a un Telegram que ya se verificó con un inicio de sesión
        -- completo (contraseña + código). Evita que alguien vincule su propio Telegram a
        -- una cuenta ajena desde el bot y luego cambie la contraseña.
        IF NOT EXISTS (SELECT 1 FROM dbo.Notificaciones WHERE UsuarioID = @Usr AND Tipo = 'TelegramVerificado')
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, CAST(NULL AS BIGINT) AS TelegramChatId, @Nombre AS Nombre, 'SinVerificar' AS Motivo;
            RETURN;
        END
        -- Anti-abuso: un código por minuto como máximo.
        IF EXISTS (SELECT 1 FROM dbo.CodigosVerificacionBot
                   WHERE UsuarioID = @Usr AND Codigo LIKE 'R%' AND ISNULL(Usado, 0) = 0
                     AND FechaExpiracion > DATEADD(MINUTE, @MinutosVigencia - 1, dbo.fn_AhoraMexico()))
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, @Chat AS TelegramChatId, @Nombre AS Nombre, 'Reciente' AS Motivo;
            RETURN;
        END

        BEGIN TRY
            BEGIN TRANSACTION;
            UPDATE dbo.CodigosVerificacionBot SET Usado = 1
            WHERE UsuarioID = @Usr AND Codigo LIKE 'R%' AND ISNULL(Usado, 0) = 0;

            INSERT INTO dbo.CodigosVerificacionBot (UsuarioID, Codigo, FechaExpiracion, Usado)
            VALUES (@Usr, 'R' + @Codigo, DATEADD(MINUTE, @MinutosVigencia, dbo.fn_AhoraMexico()), 0);
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH

        SELECT CAST(1 AS BIT) AS Enviar, @Chat AS TelegramChatId, @Nombre AS Nombre, 'OK' AS Motivo;
        RETURN;
    END

    IF @Opcion = 2
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            DECLARE @CodigoID INT = (
                SELECT TOP (1) CodigoID FROM dbo.CodigosVerificacionBot WITH (UPDLOCK, ROWLOCK)
                WHERE UsuarioID = @Usr AND Codigo = 'R' + ISNULL(@Codigo, '')
                  AND ISNULL(Usado, 0) = 0 AND FechaExpiracion >= dbo.fn_AhoraMexico()
                ORDER BY FechaExpiracion DESC);

            IF @Usr IS NULL OR @CodigoID IS NULL
                RAISERROR('El código es inválido o ya expiró. Solicita uno nuevo.', 16, 1);
            IF @PasswordHash IS NULL OR LEN(@PasswordHash) = 0
                RAISERROR('La nueva contraseña no es válida.', 16, 1);

            UPDATE dbo.CodigosVerificacionBot SET Usado = 1
            WHERE UsuarioID = @Usr AND Codigo LIKE 'R%' AND ISNULL(Usado, 0) = 0;

            UPDATE dbo.Usuarios SET PasswordHash = @PasswordHash WHERE UsuarioID = @Usr;

            COMMIT TRANSACTION;
            SELECT @Chat AS TelegramChatId, @Nombre AS Nombre;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH
    END
END;
GO

-- ---------------------------------------------------------------------
-- sp_MarcarTelegramVerificado: se llama tras un inicio de sesión exitoso
-- (contraseña + código del bot). Registra una sola vez que el Telegram
-- vinculado pertenece de verdad al dueño de la cuenta.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_MarcarTelegramVerificado
    @UsuarioID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.Usuarios WHERE UsuarioID = @UsuarioID AND TelegramChatId IS NOT NULL)
       AND NOT EXISTS (SELECT 1 FROM dbo.Notificaciones WHERE UsuarioID = @UsuarioID AND Tipo = 'TelegramVerificado')
    BEGIN
        INSERT INTO dbo.Notificaciones (UsuarioID, Mensaje, Tipo, FechaEnvio, Leido)
        VALUES (@UsuarioID, 'Telegram verificado con inicio de sesión', 'TelegramVerificado', dbo.fn_AhoraMexico(), 1);
    END
END;
GO
