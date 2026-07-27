using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clipify.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_jobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DefinitionKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                    RetryOfJobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    WorkflowId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ParentJobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProgressJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LogPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseAcquiredAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                    LeaseExpiresAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                    HeartbeatAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelRequestedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media_artifacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JobId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_artifacts_media_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "media_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_artifacts_job",
                table: "media_artifacts",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "ix_media_jobs_created",
                table: "media_jobs",
                column: "CreatedAtUnixMs");

            migrationBuilder.CreateIndex(
                name: "ix_media_jobs_lease_expires",
                table: "media_jobs",
                column: "LeaseExpiresAtUnixMs");

            migrationBuilder.CreateIndex(
                name: "ix_media_jobs_queue",
                table: "media_jobs",
                columns: new[] { "State", "Priority", "CreatedAtUnixMs", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_artifacts");

            migrationBuilder.DropTable(
                name: "media_jobs");
        }
    }
}
