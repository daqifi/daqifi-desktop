﻿// <auto-generated />
using System;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Daqifi.Desktop.Migrations
{
    [DbContext(typeof(LoggingContext))]
    partial class LoggingContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("Daqifi.Desktop.Channel.DataSample", b =>
                {
                    b.Property<int>("ID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("ID"));

                    b.Property<string>("ChannelName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Color")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("DeviceName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int>("LoggingSessionID")
                        .HasColumnType("int");

                    b.Property<long>("TimestampTicks")
                        .HasColumnType("bigint");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<double>("Value")
                        .HasColumnType("float");

                    b.HasKey("ID");

                    b.HasIndex("LoggingSessionID");

                    b.ToTable("Samples");
                });

            modelBuilder.Entity("Daqifi.Desktop.Logger.LoggingSession", b =>
                {
                    b.Property<int>("ID")
                        .HasColumnType("int");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("SessionStart")
                        .HasColumnType("datetime2");

                    b.HasKey("ID");

                    b.ToTable("Sessions");
                });

            modelBuilder.Entity("Daqifi.Desktop.Channel.DataSample", b =>
                {
                    b.HasOne("Daqifi.Desktop.Logger.LoggingSession", "LoggingSession")
                        .WithMany("DataSamples")
                        .HasForeignKey("LoggingSessionID")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("LoggingSession");
                });

            modelBuilder.Entity("Daqifi.Desktop.Logger.LoggingSession", b =>
                {
                    b.Navigation("DataSamples");
                });
#pragma warning restore 612, 618
        }
    }
}
