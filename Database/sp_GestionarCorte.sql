-- ==========================================================
-- PROCEDIMIENTO ALMACENADO PARA GESTIONAR CORTES / SERVICIOS
-- @Opcion 1: Insertar o actualizar
--   - Con @CorteId > 0 actualiza ese registro por ID (permite renombrar).
--   - Sin @CorteId mantiene el comportamiento original: upsert por @Nombre.
-- @Opcion 2: Listar servicios activos
-- ==========================================================
CREATE OR ALTER PROCEDURE dbo.sp_GestionarCorte
    @Opcion INT,
    @Nombre VARCHAR(100) = NULL,
    @Descripcion VARCHAR(255) = NULL,
    @Precio DECIMAL(18,2) = 0.00,
    @DuracionMinutos INT = 30,
    @Estado BIT = 1,
    @CorteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Opcion = 1
    BEGIN
        IF @CorteId IS NOT NULL AND @CorteId > 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.Cortes WHERE CorteID = @CorteId)
            BEGIN
                RAISERROR('El servicio que intentas editar no existe.', 16, 1);
                RETURN;
            END

            IF EXISTS (SELECT 1 FROM dbo.Cortes WHERE Nombre = @Nombre AND CorteID <> @CorteId)
            BEGIN
                RAISERROR('Ya existe otro servicio con ese nombre.', 16, 1);
                RETURN;
            END

            UPDATE dbo.Cortes
            SET Nombre = @Nombre, Descripcion = @Descripcion, Precio = @Precio,
                DuracionMinutos = @DuracionMinutos, Estado = @Estado
            WHERE CorteID = @CorteId;
        END
        -- Comportamiento original: si ya existe un corte con el mismo nombre, se actualiza; si no, se inserta
        ELSE IF EXISTS (SELECT 1 FROM dbo.Cortes WHERE Nombre = @Nombre)
        BEGIN
            UPDATE dbo.Cortes
            SET Descripcion = @Descripcion, Precio = @Precio, DuracionMinutos = @DuracionMinutos, Estado = @Estado
            WHERE Nombre = @Nombre;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.Cortes (Nombre, Descripcion, Precio, DuracionMinutos, Estado)
            VALUES (@Nombre, @Descripcion, @Precio, @DuracionMinutos, @Estado);
        END
    END
    ELSE IF @Opcion = 2
    BEGIN
        SELECT * FROM dbo.Cortes WHERE Estado = 1;
    END
END;
GO
