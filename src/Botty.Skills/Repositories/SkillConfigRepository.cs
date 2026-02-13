using Botty.Core.Models;
using Botty.Skills.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace Botty.Skills.Repositories;

/// <summary>
/// PostgreSQL repository for skill configuration storage.
/// </summary>
public class SkillConfigRepository : ISkillConfigRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SkillConfigRepository> _logger;

    public SkillConfigRepository(
        string connectionString,
        ILogger<SkillConfigRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<List<SkillConfigValue>> GetAllValuesAsync(string skillId, CancellationToken ct = default)
    {
        var values = new List<SkillConfigValue>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "SELECT id, skill_id, key, value, is_sensitive, updated_at FROM skill_config WHERE skill_id = @skillId",
            connection);
        command.Parameters.AddWithValue("skillId", skillId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(new SkillConfigValue
            {
                Id = reader.GetGuid(0),
                SkillId = reader.GetString(1),
                Key = reader.GetString(2),
                Value = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsSensitive = reader.GetBoolean(4),
                UpdatedAt = reader.GetDateTime(5)
            });
        }

        return values;
    }

    public async Task SetValueAsync(SkillConfigValue value, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Upsert based on skill_id and key
        await using var command = new NpgsqlCommand(@"
            INSERT INTO skill_config (id, skill_id, key, value, is_sensitive, updated_at)
            VALUES (@id, @skillId, @key, @value, @isSensitive, @updatedAt)
            ON CONFLICT (skill_id, key) 
            DO UPDATE SET value = @value, is_sensitive = @isSensitive, updated_at = @updatedAt",
            connection);

        command.Parameters.AddWithValue("id", value.Id);
        command.Parameters.AddWithValue("skillId", value.SkillId);
        command.Parameters.AddWithValue("key", value.Key);
        command.Parameters.AddWithValue("value", (object?)value.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("isSensitive", value.IsSensitive);
        command.Parameters.AddWithValue("updatedAt", value.UpdatedAt);

        await command.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Set config value {Key} for skill {SkillId}", value.Key, value.SkillId);
    }

    public async Task DeleteValueAsync(string skillId, string key, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "DELETE FROM skill_config WHERE skill_id = @skillId AND key = @key",
            connection);
        command.Parameters.AddWithValue("skillId", skillId);
        command.Parameters.AddWithValue("key", key);

        await command.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Deleted config value {Key} for skill {SkillId}", key, skillId);
    }

    public async Task<bool> HasValueAsync(string skillId, string key, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM skill_config WHERE skill_id = @skillId AND key = @key)",
            connection);
        command.Parameters.AddWithValue("skillId", skillId);
        command.Parameters.AddWithValue("key", key);

        var result = await command.ExecuteScalarAsync(ct);
        return result is bool exists && exists;
    }

    /// <summary>
    /// Ensures the skill_config table exists.
    /// </summary>
    public async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS skill_config (
                id UUID PRIMARY KEY,
                skill_id VARCHAR(100) NOT NULL,
                key VARCHAR(100) NOT NULL,
                value TEXT,
                is_sensitive BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                UNIQUE(skill_id, key)
            );
            
            CREATE INDEX IF NOT EXISTS idx_skill_config_skill_id ON skill_config(skill_id);",
            connection);

        await command.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Ensured skill_config table exists");
    }
}
