using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lumina_Reserve_2.Models
{
    public partial class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext()
        {
        }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Cita> Citas { get; set; }
        public virtual DbSet<CodigosVerificacionBot> CodigosVerificacionBots { get; set; }
        public virtual DbSet<Corte> Cortes { get; set; }
        public virtual DbSet<Empleado> Empleados { get; set; }
        public virtual DbSet<Gasto> Gastos { get; set; }
        public virtual DbSet<Notificacione> Notificaciones { get; set; }
        public virtual DbSet<Pago> Pagos { get; set; }
        public virtual DbSet<Resena> Resenas { get; set; }
        public virtual DbSet<Role> Roles { get; set; }
        public virtual DbSet<Sucursale> Sucursales { get; set; }
        public virtual DbSet<Usuario> Usuarios { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#pragma warning disable CS1030
                optionsBuilder.UseSqlServer("Server=.\\SQLEXPRESS; Database=LuminaReserveDB; User Id=SA; Password=2811; TrustServerCertificate=True;");
#pragma warning restore CS1030
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Cita>(entity =>
            {
                entity.HasKey(e => e.CitaId).HasName("PK_Citas_F0E2D9F222D72B55");
                entity.Property(e => e.CitaId).HasColumnName("CitaID");
                entity.Property(e => e.EmpleadoId).HasColumnName("EmpleadoID");
                entity.Property(e => e.Estado)
                    .HasMaxLength(30)
                    .IsUnicode(false)
                    .HasDefaultValue("Pendiente");
                entity.Property(e => e.FechaCreacion)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnType("datetime");
                entity.Property(e => e.FechaHora).HasColumnType("datetime");
                entity.Property(e => e.SucursalId).HasColumnName("SucursalID");
                entity.Property(e => e.UsuarioId).HasColumnName("UsuarioID");
                
                entity.HasOne(d => d.Empleado).WithMany(p => p.Cita)
                    .HasForeignKey(d => d.EmpleadoId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Citas_Empleadol_42E1EEFE");
                
                entity.HasOne(d => d.Sucursal).WithMany(p => p.Cita)
                    .HasForeignKey(d => d.SucursalId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Citas_Sucursall_43D61337");
                
                entity.HasOne(d => d.Usuario).WithMany(p => p.Cita)
                    .HasForeignKey(d => d.UsuarioId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Citas_UsuarioID_41EDCAC5");
                
                entity.HasMany(d => d.Cortes).WithMany(p => p.Cita)
                    .UsingEntity<Dictionary<string, object>>(
                        "CitaCorte",
                        r => r.HasOne<Corte>().WithMany().HasForeignKey("CorteID").HasConstraintName("FK_CitaCorte_Corte_47A6A41B"),
                        l => l.HasOne<Cita>().WithMany().HasForeignKey("CitaID").HasConstraintName("FK_CitaCorte_Cital_46B27FE2"),
                        j =>
                        {
                            j.HasKey("CitaID", "CorteID").HasName("PK_CitaCort_F9E7DE71554BBB09");
                            j.ToTable("CitaCortes");
                            j.IndexerProperty<int>("CitaID").HasColumnName("CitaID");
                            j.IndexerProperty<int>("CorteID").HasColumnName("CorteID");
                        });
            });

            modelBuilder.Entity<CodigosVerificacionBot>(entity =>
            {
                entity.HasKey(e => e.CodigoId).HasName("PK_CodigosV_DB319CF35FC0A6AE");
                entity.ToTable("CodigosVerificacionBot");
                entity.Property(e => e.CodigoId).HasColumnName("CodigoID");
                entity.Property(e => e.Codigo)
                    .HasMaxLength(10)
                    .IsUnicode(false);
                entity.Property(e => e.FechaExpiracion).HasColumnType("datetime");
                entity.Property(e => e.Usado).HasDefaultValue(false);
                entity.Property(e => e.UsuarioId).HasColumnName("UsuarioID");
                
                entity.HasOne(d => d.Usuario).WithMany(p => p.CodigosVerificacionBots)
                    .HasForeignKey(d => d.UsuarioId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_CodigosVe_Usuar_5F7E2DAC");
            });

            modelBuilder.Entity<Corte>(entity =>
            {
                entity.HasKey(e => e.CorteId).HasName("PK_Cortes_905078314B0C09FD");
                entity.Property(e => e.CorteId).HasColumnName("CorteID");
                entity.Property(e => e.Descripcion)
                    .HasMaxLength(255)
                    .IsUnicode(false);
                entity.Property(e => e.DuracionMinutos).HasDefaultValue(30);
                entity.Property(e => e.Estado).HasDefaultValue(true);
                entity.Property(e => e.Nombre)
                    .HasMaxLength(100)
                    .IsUnicode(false);
                entity.Property(e => e.Precio).HasColumnType("decimal(10, 2)");
            });

            modelBuilder.Entity<Empleado>(entity =>
            {
                entity.HasKey(e => e.EmpleadoId).HasName("PK_Empleado_958BE6F0500C37ED");
                entity.Property(e => e.EmpleadoId).HasColumnName("EmpleadoID");
                entity.Property(e => e.Especialidad)
                    .HasMaxLength(100)
                    .IsUnicode(false);
                entity.Property(e => e.Estado).HasDefaultValue(true);
                entity.Property(e => e.SucursalId).HasColumnName("SucursalID");
                entity.Property(e => e.UsuarioId).HasColumnName("UsuarioID");
                
                entity.HasOne(d => d.Sucursal).WithMany(p => p.Empleados)
                    .HasForeignKey(d => d.SucursalId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Empleados_Sucur_395884C4");
                
                entity.HasOne(d => d.Usuario).WithMany(p => p.Empleados)
                    .HasForeignKey(d => d.UsuarioId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Empleados_Usuar_3864608B");
            });

            modelBuilder.Entity<Gasto>(entity =>
            {
                entity.HasKey(e => e.GastoId).HasName("PK_Gastos_815BB110633EBBE5");
                entity.Property(e => e.GastoId).HasColumnName("GastoID");
                entity.Property(e => e.Concepto)
                    .HasMaxLength(150)
                    .IsUnicode(false);
                entity.Property(e => e.Fecha)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnType("datetime");
                entity.Property(e => e.Monto).HasColumnType("decimal(10, 2)");
                entity.Property(e => e.SucursalId).HasColumnName("SucursalID");
                
                entity.HasOne(d => d.Sucursal).WithMany(p => p.Gastos)
                    .HasForeignKey(d => d.SucursalId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Gastos_Sucursal_503BEA1C");
            });

            modelBuilder.Entity<Notificacione>(entity =>
            {
                entity.HasKey(e => e.NotificacionId).HasName("PK_Notifica_BCC120C499022805");
                entity.Property(e => e.NotificacionId).HasColumnName("NotificacionID");
                entity.Property(e => e.FechaEnvio)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnType("datetime");
                entity.Property(e => e.Leido).HasDefaultValue(false);
                entity.Property(e => e.Mensaje)
                    .HasMaxLength(255)
                    .IsUnicode(false);
                entity.Property(e => e.Tipo)
                    .HasMaxLength(50)
                    .IsUnicode(false)
                    .HasDefaultValue("Telegram");
                entity.Property(e => e.UsuarioId).HasColumnName("UsuarioID");
                
                entity.HasOne(d => d.Usuario).WithMany(p => p.Notificaciones)
                    .HasForeignKey(d => d.UsuarioId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Notificac_Usuar_5BAD9CC8");
            });

            modelBuilder.Entity<Pago>(entity =>
            {
                entity.HasKey(e => e.PagoId).HasName("PK_Pagos_F00B61583A6ED9B1");
                entity.Property(e => e.PagoId).HasColumnName("PagoID");
                entity.Property(e => e.CitaId).HasColumnName("CitaID");
                entity.Property(e => e.EstadoPago)
                    .HasMaxLength(30)
                    .IsUnicode(false)
                    .HasDefaultValue("Completado");
                entity.Property(e => e.FechaPago)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnType("datetime");
                entity.Property(e => e.MetodoPago)
                    .HasMaxLength(50)
                    .IsUnicode(false);
                entity.Property(e => e.Monto).HasColumnType("decimal(10, 2)");
                
                entity.HasOne(d => d.Cita).WithMany(p => p.Pagos)
                    .HasForeignKey(d => d.CitaId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Pagos_CitalD_4C6B5938");
            });

            modelBuilder.Entity<Resena>(entity =>
            {
                entity.HasKey(e => e.ResenaId).HasName("PK_Resenas_DCC1B117DEC14777");
                entity.Property(e => e.ResenaId).HasColumnName("ResenaID");
                entity.Property(e => e.CitaId).HasColumnName("CitaID");
                entity.Property(e => e.Comentario)
                    .HasMaxLength(500)
                    .IsUnicode(false);
                entity.Property(e => e.Fecha)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnType("datetime");
                entity.Property(e => e.UsuarioId).HasColumnName("UsuarioID");
                
                entity.HasOne(d => d.Cita).WithMany(p => p.Resenas)
                    .HasForeignKey(d => d.CitaId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Resenas_CitalD_55009F39");
                
                entity.HasOne(d => d.Usuario).WithMany(p => p.Resenas)
                    .HasForeignKey(d => d.UsuarioId)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("FK_Resenas_Usuario_55F4C372");
            });

            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasKey(e => e.RolId).HasName("PK_Roles_F92302D182645950");
                entity.HasIndex(e => e.Nombre, "UQ_Roles_75E3EFCF036A6B18").IsUnique();
                entity.Property(e => e.RolId).HasColumnName("RolID");
                entity.Property(e => e.Nombre)
                    .HasMaxLength(50)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<Sucursale>(entity =>
            {
                entity.HasKey(e => e.SucursalId).HasName("PK_Sucursal_6CB48281D843AF6A");
                entity.Property(e => e.SucursalId).HasColumnName("SucursalID");
                entity.Property(e => e.Direccion)
                    .HasMaxLength(200)
                    .IsUnicode(false);
                entity.Property(e => e.Estado).HasDefaultValue(true);
                entity.Property(e => e.Nombre)
                    .HasMaxLength(100)
                    .IsUnicode(false);
                entity.Property(e => e.Telefono)
                    .HasMaxLength(20)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<Usuario>(entity =>
            {
                entity.HasKey(e => e.UsuarioId).HasName("PK_Usuarios_2B3DE798A4C518C2");
                entity.HasIndex(e => e.Correo, "UQ_Usuarios_60695A19999EE422").IsUnique();
                entity.Property(e => e.UsuarioId).HasColumnName("UsuarioID");
                entity.Property(e => e.Correo)
                    .HasMaxLength(100)
                    .IsUnicode(false);
                entity.Property(e => e.Estado).HasDefaultValue(true);
                entity.Property(e => e.FechaRegistro)
                    .HasDefaultValueSql("(getdate())")
                    .HasColumnType("datetime");
                entity.Property(e => e.Nombre)
                    .HasMaxLength(100)
                    .IsUnicode(false);
                entity.Property(e => e.PasswordHash)
                    .HasMaxLength(256)
                    .IsUnicode(false);
                entity.Property(e => e.Telefono)
                    .HasMaxLength(20)
                    .IsUnicode(false);
                
                entity.HasMany(d => d.Rols).WithMany(p => p.Usuarios)
                    .UsingEntity<Dictionary<string, object>>(
                        "UsuarioRole",
                        r => r.HasOne<Role>().WithMany().HasForeignKey("RolID").HasConstraintName("FK_UsuarioRo_RolID_31B762FC"),
                        l => l.HasOne<Usuario>().WithMany().HasForeignKey("UsuarioID").HasConstraintName("FK_UsuarioRo_Usuar_30C33EC3"),
                        j =>
                        {
                            j.HasKey("UsuarioID", "RolID").HasName("PK_UsuarioR_24AFD7B5DBED7695");
                            j.ToTable("UsuarioRoles");
                            j.IndexerProperty<int>("UsuarioID").HasColumnName("UsuarioID");
                            j.IndexerProperty<int>("RolID").HasColumnName("RolID");
                        });
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}