namespace Daqifi.Desktop.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialMigration : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.DataSamples",
                c => new
                    {
                        ID = c.Int(nullable: false, identity: true),
                        LoggingSessionID = c.Int(nullable: false),
                        Value = c.Double(nullable: false),
                        TimestampTicks = c.Long(nullable: false),
                        DeviceName = c.String(maxLength: 4000),
                        ChannelName = c.String(maxLength: 4000),
                        Color = c.String(maxLength: 4000),
                        Type = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.ID)
                .ForeignKey("dbo.LoggingSessions", t => t.LoggingSessionID, cascadeDelete: true)
                .Index(t => t.LoggingSessionID);
            
            CreateTable(
                "dbo.LoggingSessions",
                c => new
                    {
                        ID = c.Int(nullable: false),
                        SessionStart = c.DateTime(nullable: false),
                        Name = c.String(maxLength: 4000),
                    })
                .PrimaryKey(t => t.ID);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.DataSamples", "LoggingSessionID", "dbo.LoggingSessions");
            DropIndex("dbo.DataSamples", new[] { "LoggingSessionID" });
            DropTable("dbo.LoggingSessions");
            DropTable("dbo.DataSamples");
        }
    }
}
