-- =====================================================================
-- LUMINA RESERVE - PERSONAL (alta/edición de barberos) Y RESUMEN DIARIO
-- Ejecutar sobre LuminaReserveDB (CREATE OR ALTER: se puede re-ejecutar).
-- Desde sqlcmd usar -f 65001 para que los acentos se guarden bien.
-- =====================================================================

-- ---------------------------------------------------------------------
-- sp_GestionarEmpleado (ampliado; la opción 1 conserva su comportamiento original)
--   @Opcion 1: Insertar empleado para un usuario existente (@UsuarioID)
--   @Opcion 2: Actualizar barbero (@EmpleadoID): sucursal, especialidad, horario,
--              y nombre / teléfono de su usuario
--   @Opcion 3: Alta completa: crea el usuario (si el correo no existe) y el empleado.
--              Si el correo ya existe y no es barbero, reutiliza ese usuario.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GestionarEmpleado
    @Opcion INT,
    @UsuarioID INT = NULL,
    @SucursalID INT = 1,
    @Especialidad VARCHAR(100) = NULL,
    @HorarioInicio TIME = '10:00:00',
    @HorarioFin TIME = '20:00:00',
    @Estado BIT = 1,
    @EmpleadoID INT = NULL,
    @Nombre VARCHAR(100) = NULL,
    @Correo VARCHAR(100) = NULL,
    @Telefono VARCHAR(20) = NULL,
    @PasswordHash VARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Opcion = 1
    BEGIN
        INSERT INTO dbo.Empleados (UsuarioID, SucursalID, Especialidad, HorarioInicio, HorarioFin, Estado)
        VALUES (@UsuarioID, @SucursalID, @Especialidad, @HorarioInicio, @HorarioFin, @Estado);

        SELECT SCOPE_IDENTITY() AS NuevoEmpleadoID;
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Sucursales WHERE SucursalID = @SucursalID)
            RAISERROR('La sucursal seleccionada no es válida.', 16, 1);
        IF @HorarioInicio IS NULL OR @HorarioFin IS NULL OR @HorarioInicio >= @HorarioFin
            RAISERROR('El horario de salida debe ser posterior al de entrada.', 16, 1);

        IF @Opcion = 2
        BEGIN
            DECLARE @Usr INT = (SELECT UsuarioID FROM dbo.Empleados WHERE EmpleadoID = @EmpleadoID);
            IF @Usr IS NULL
                RAISERROR('El barbero no existe.', 16, 1);
            IF LEN(LTRIM(RTRIM(ISNULL(@Nombre, '')))) < 2
                RAISERROR('Escribe el nombre del barbero.', 16, 1);

            UPDATE dbo.Empleados
            SET SucursalID = @SucursalID,
                Especialidad = NULLIF(LTRIM(RTRIM(@Especialidad)), ''),
                HorarioInicio = @HorarioInicio,
                HorarioFin = @HorarioFin
            WHERE EmpleadoID = @EmpleadoID;

            UPDATE dbo.Usuarios
            SET Nombre = LTRIM(RTRIM(@Nombre)),
                Telefono = NULLIF(LTRIM(RTRIM(@Telefono)), '')
            WHERE UsuarioID = @Usr;

            COMMIT TRANSACTION;
            SELECT @EmpleadoID AS EmpleadoID, CAST(0 AS BIT) AS UsuarioCreado;
            RETURN;
        END

        IF @Opcion = 3
        BEGIN
            SET @Correo = LOWER(LTRIM(RTRIM(ISNULL(@Correo, ''))));
            IF LEN(LTRIM(RTRIM(ISNULL(@Nombre, '')))) < 2
                RAISERROR('Escribe el nombre del barbero.', 16, 1);
            IF @Correo NOT LIKE '%_@_%._%'
                RAISERROR('Escribe un correo válido.', 16, 1);

            DECLARE @UsuarioExistente INT = (SELECT UsuarioID FROM dbo.Usuarios WHERE LOWER(Correo) = @Correo);
            DECLARE @Creado BIT = 0;

            IF @UsuarioExistente IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.Empleados WHERE UsuarioID = @UsuarioExistente)
                    RAISERROR('Ese correo ya pertenece a un barbero registrado.', 16, 1);
            END
            ELSE
            BEGIN
                IF @PasswordHash IS NULL OR LEN(@PasswordHash) = 0
                    RAISERROR('Asigna una contraseña inicial para el nuevo usuario.', 16, 1);

                INSERT INTO dbo.Usuarios (Nombre, Correo, PasswordHash, Telefono, FechaRegistro, Estado)
                VALUES (LTRIM(RTRIM(@Nombre)), @Correo, @PasswordHash, NULLIF(LTRIM(RTRIM(@Telefono)), ''), dbo.fn_AhoraMexico(), 1);
                SET @UsuarioExistente = CAST(SCOPE_IDENTITY() AS INT);
                SET @Creado = 1;
            END

            INSERT INTO dbo.Empleados (UsuarioID, SucursalID, Especialidad, HorarioInicio, HorarioFin, Estado)
            VALUES (@UsuarioExistente, @SucursalID, NULLIF(LTRIM(RTRIM(@Especialidad)), ''), @HorarioInicio, @HorarioFin, 1);

            DECLARE @Nuevo INT = CAST(SCOPE_IDENTITY() AS INT);
            COMMIT TRANSACTION;
            SELECT @Nuevo AS EmpleadoID, @Creado AS UsuarioCreado;
            RETURN;
        END

        RAISERROR('Opción no válida.', 16, 1);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ---------------------------------------------------------------------
-- sp_ObtenerBarberosAdmin (agrega SucursalID y Telefono para el formulario de edición)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ObtenerBarberosAdmin
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        e.EmpleadoId,
        u.Nombre AS NombreBarbero,
        u.Correo,
        s.Nombre AS SucursalAsignada,
        e.Especialidad,
        e.HorarioInicio,
        e.HorarioFin,
        CAST(ISNULL(e.Estado, 1) AS BIT) AS Activo,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.EmpleadoID = e.EmpleadoID AND c.FechaHora >= dbo.fn_AhoraMexico()
            AND c.Estado IN ('Pendiente', 'Confirmada')) AS CitasProximas,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.EmpleadoID = e.EmpleadoID AND c.Estado = 'Completada') AS CitasCompletadas,
        e.SucursalID,
        u.Telefono
    FROM dbo.Empleados e
    INNER JOIN dbo.Usuarios u ON e.UsuarioId = u.UsuarioId
    INNER JOIN dbo.Sucursales s ON e.SucursalId = s.SucursalId
    ORDER BY s.SucursalID, u.Nombre;
END;
GO

-- ---------------------------------------------------------------------
-- sp_ResumenDiarioAdmin: datos del resumen diario por Telegram
--   Resultado 1: por sucursal -> citas de hoy, por atender, ingresos/gastos de ayer
--   Resultado 2: detalle de las citas de hoy (no canceladas)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ResumenDiarioAdmin
    @Fecha DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Hoy DATETIME = CAST(@Fecha AS DATETIME);
    DECLARE @Manana DATETIME = DATEADD(DAY, 1, @Hoy);
    DECLARE @Ayer DATETIME = DATEADD(DAY, -1, @Hoy);

    SELECT
        s.SucursalID,
        s.Nombre,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Hoy AND c.FechaHora < @Manana
            AND c.Estado <> 'Cancelada')                                   AS CitasHoy,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Hoy AND c.FechaHora < @Manana
            AND c.Estado IN ('Pendiente', 'Confirmada'))                   AS PorAtenderHoy,
        (SELECT ISNULL(SUM(p.Monto), 0) FROM dbo.Pagos p
          INNER JOIN dbo.Citas c ON c.CitaID = p.CitaID
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Ayer AND c.FechaHora < @Hoy
            AND p.EstadoPago = 'Completado')                               AS IngresosAyer,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Ayer AND c.FechaHora < @Hoy
            AND c.Estado = 'Completada')                                   AS CompletadasAyer,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Ayer AND c.FechaHora < @Hoy
            AND c.Estado = 'Cancelada')                                    AS CanceladasAyer,
        (SELECT ISNULL(SUM(g.Monto), 0) FROM dbo.Gastos g
          WHERE g.SucursalID = s.SucursalID AND g.Fecha >= @Ayer AND g.Fecha < @Hoy) AS GastosAyer
    FROM dbo.Sucursales s
    WHERE ISNULL(s.Estado, 1) = 1
    ORDER BY s.SucursalID;

    SELECT
        s.Nombre                                                       AS Sucursal,
        c.FechaHora,
        c.Estado,
        COALESCE(u.Nombre, c.NombreClienteExterno, 'Cliente de paso')  AS Cliente,
        ue.Nombre                                                      AS Barbero,
        ISNULL((SELECT STRING_AGG(co.Nombre, ', ')
                  FROM dbo.CitaCortes cc INNER JOIN dbo.Cortes co ON co.CorteID = cc.CorteID
                 WHERE cc.CitaID = c.CitaID), '')                      AS Servicios
    FROM dbo.Citas c
    INNER JOIN dbo.Sucursales s ON s.SucursalID = c.SucursalID
    INNER JOIN dbo.Empleados e  ON e.EmpleadoID = c.EmpleadoID
    INNER JOIN dbo.Usuarios ue  ON ue.UsuarioID = e.UsuarioID
    LEFT  JOIN dbo.Usuarios u   ON u.UsuarioID = c.UsuarioID
    WHERE c.FechaHora >= @Hoy AND c.FechaHora < @Manana AND c.Estado <> 'Cancelada'
    ORDER BY s.SucursalID, c.FechaHora;
END;
GO

-- ---------------------------------------------------------------------
-- sp_ControlResumenDiario: evita enviar el resumen dos veces el mismo día
--   @Opcion 1: ¿ya se envió el resumen de @Fecha?  -> YaEnviado (bit)
--   @Opcion 2: registra el envío en Notificaciones (Tipo = 'ResumenDiario')
--              a nombre del administrador (el que tenga @ChatId, o el primero)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ControlResumenDiario
    @Opcion INT,
    @Fecha DATE,
    @ChatId BIGINT = NULL,
    @Mensaje VARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Fecha AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, @Ini);

    IF @Opcion = 1
    BEGIN
        SELECT CAST(CASE WHEN EXISTS (
                   SELECT 1 FROM dbo.Notificaciones
                   WHERE Tipo = 'ResumenDiario' AND FechaEnvio >= @Ini AND FechaEnvio < @Fin)
               THEN 1 ELSE 0 END AS BIT) AS YaEnviado;
        RETURN;
    END

    IF @Opcion = 2
    BEGIN
        DECLARE @Admin INT = (
            SELECT TOP (1) u.UsuarioID
            FROM dbo.Usuarios u
            INNER JOIN dbo.UsuarioRoles ur ON ur.UsuarioID = u.UsuarioID
            INNER JOIN dbo.Roles r ON r.RolID = ur.RolID
            WHERE r.Nombre IN ('Admin', 'Administrador')
            ORDER BY CASE WHEN u.TelegramChatId = @ChatId THEN 0 ELSE 1 END, u.UsuarioID);

        IF @Admin IS NULL
        BEGIN
            RAISERROR('No hay un usuario administrador para registrar el resumen.', 16, 1);
            RETURN;
        END

        INSERT INTO dbo.Notificaciones (UsuarioID, Mensaje, Tipo, FechaEnvio, Leido)
        VALUES (@Admin, LEFT(ISNULL(@Mensaje, 'Resumen diario enviado'), 255), 'ResumenDiario', dbo.fn_AhoraMexico(), 1);
    END
END;
GO
