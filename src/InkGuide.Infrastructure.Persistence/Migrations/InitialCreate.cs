using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace InkGuide.Infrastructure.Persistence.Migrations;

/// <summary>Initial schema migration (matches <see cref="InkGuideDbContext"/> model v1).</summary>
public sealed partial class InitialCreate : Migration
{
    private static readonly string[] ListingColumns = new[] { "AccountId", "ParentId", "IsFolder", "Name" };
    private static readonly string[] ScopeColumns = new[] { "AccountId", "RepoId", "ProjectId" };
    private static readonly string[] DuplicateColumns = new[] { "AccountId", "Md5", "Size" };
    private static readonly string[] TrashedColumns = new[] { "AccountId", "Trashed" };
    private static readonly string[] OutboxDueColumns = new[] { "State", "NextAttemptAtUtc" };
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 128, nullable: false),
                Email = table.Column<string>(nullable: false),
                DisplayName = table.Column<string>(nullable: false),
                ScopeMode = table.Column<string>(nullable: false),
                IsActive = table.Column<bool>(nullable: false),
                ReauthRequired = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: table => { table.PrimaryKey("PK_Accounts", x => x.Id); });

        migrationBuilder.CreateTable(
            name: "Nodes",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 256, nullable: false),
                AccountId = table.Column<string>(nullable: false),
                ParentId = table.Column<string>(nullable: true),
                Name = table.Column<string>(nullable: false),
                IsFolder = table.Column<bool>(nullable: false),
                MimeType = table.Column<string>(nullable: true),
                Size = table.Column<long>(nullable: false),
                Md5 = table.Column<string>(nullable: true),
                Version = table.Column<string>(nullable: true),
                HeadRevisionId = table.Column<string>(nullable: true),
                ModifiedTimeUtc = table.Column<DateTimeOffset>(nullable: false),
                Trashed = table.Column<bool>(nullable: false),
                RepoId = table.Column<string>(nullable: true),
                ProjectId = table.Column<string>(nullable: true),
                PathText = table.Column<string>(nullable: false),
                Description = table.Column<string>(nullable: true),
                WebViewLink = table.Column<string>(nullable: true),
                SyncedAtUtc = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: table => { table.PrimaryKey("PK_Nodes", x => x.Id); });

        migrationBuilder.CreateIndex("IX_Nodes_Listing", "Nodes", ListingColumns);
        migrationBuilder.CreateIndex("IX_Nodes_Scope", "Nodes", ScopeColumns);
        migrationBuilder.CreateIndex("IX_Nodes_Duplicates", "Nodes", DuplicateColumns);
        migrationBuilder.CreateIndex("IX_Nodes_Trashed", "Nodes", TrashedColumns);

        migrationBuilder.CreateTable(
            name: "OutboxOperations",
            columns: table => new
            {
                OpId = table.Column<string>(nullable: false),
                AccountId = table.Column<string>(nullable: false),
                OperationType = table.Column<string>(nullable: false),
                PayloadJson = table.Column<string>(nullable: false),
                State = table.Column<string>(nullable: false),
                Attempts = table.Column<int>(nullable: false),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(nullable: false),
                LastError = table.Column<string>(nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
            },
            constraints: table => { table.PrimaryKey("PK_OutboxOperations", x => x.OpId); });

        migrationBuilder.CreateIndex("IX_Outbox_Due", "OutboxOperations", OutboxDueColumns);

        migrationBuilder.Sql("CREATE VIRTUAL TABLE IF NOT EXISTS node_fts USING fts5(nodeId UNINDEXED, name, summary, tags, path, content);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("DROP TABLE IF EXISTS node_fts;");
        migrationBuilder.DropTable(name: "OutboxOperations");
        migrationBuilder.DropTable(name: "Nodes");
        migrationBuilder.DropTable(name: "Accounts");
    }
}
