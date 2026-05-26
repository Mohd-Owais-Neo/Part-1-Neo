using Microsoft.Data.SqlClient;
using NEO.Core.Models;
using NEO.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEO.Core.Data
{
    public class DatabaseHelper
    {
        private readonly string _connectionString;

        public DatabaseHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        // =============================================
        // CONNECTION TEST
        // =============================================
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                return false;
            }
        }

        // =============================================
        // RUN LOG
        // =============================================
        public async Task InsertRunLogAsync(RunLog log)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"INSERT INTO Run_Decision_Log
                        (run_id, business_date, stage, message, status)
                        VALUES
                        (@RunId, @BusinessDate, @Stage, @Message, @Status)";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RunId", log.RunId);
            cmd.Parameters.AddWithValue("@BusinessDate", log.BusinessDate);
            cmd.Parameters.AddWithValue("@Stage", log.Stage);
            cmd.Parameters.AddWithValue("@Message", log.Message);
            cmd.Parameters.AddWithValue("@Status", log.Status);

            await cmd.ExecuteNonQueryAsync();
        }

        // =============================================
        // SECTOR TABLES — Insert
        // =============================================
        public async Task InsertSectorRankingAsync(
            string tableName,
            string runId,
            DateTime businessDate,
            List<Sector> sectors)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Clear previous data for same date
            var deleteSql = $@"DELETE FROM {tableName}
                               WHERE business_date = @BusinessDate";
            using var deleteCmd = new SqlCommand(deleteSql, conn);
            deleteCmd.Parameters.AddWithValue("@BusinessDate", businessDate);
            await deleteCmd.ExecuteNonQueryAsync();

            // Insert fresh data
            foreach (var sector in sectors)
            {
                var sql = $@"INSERT INTO {tableName}
                            (run_id, business_date, sector_name, pct_change, rank)
                            VALUES
                            (@RunId, @BusinessDate, @SectorName, @PctChange, @Rank)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@RunId", runId);
                cmd.Parameters.AddWithValue("@BusinessDate", businessDate);
                cmd.Parameters.AddWithValue("@SectorName", sector.SectorName);
                cmd.Parameters.AddWithValue("@PctChange", sector.PctChange);
                cmd.Parameters.AddWithValue("@Rank", sector.Rank);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        // =============================================
        // SECTOR TABLES — Read
        // =============================================
        public async Task<List<Sector>> GetSectorRankingAsync(
            string tableName,
            string runId,
            DateTime businessDate)
        {
            var result = new List<Sector>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $@"SELECT sector_name, pct_change, rank
                         FROM {tableName}
                         WHERE run_id = @RunId
                         AND business_date = @BusinessDate
                         ORDER BY rank ASC";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RunId", runId);
            cmd.Parameters.AddWithValue("@BusinessDate", businessDate);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new Sector
                {
                    SectorName = reader["sector_name"].ToString() ?? "",
                    PctChange = Convert.ToDecimal(reader["pct_change"]),
                    Rank = Convert.ToInt32(reader["rank"])
                });
            }

            return result;
        }

        // =============================================
        // TABLE 1 — All Stocks
        // =============================================
        public async Task InsertAllStocksAsync(
            string runId,
            DateTime businessDate,
            List<Stock> stocks)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Clear previous data for same date
            var deleteSql = @"DELETE FROM Table_1_All_Stocks
                              WHERE business_date = @BusinessDate";
            using var deleteCmd = new SqlCommand(deleteSql, conn);
            deleteCmd.Parameters.AddWithValue("@BusinessDate", businessDate);
            await deleteCmd.ExecuteNonQueryAsync();

            foreach (var stock in stocks)
            {
                var sql = @"INSERT INTO Table_1_All_Stocks
                           (run_id, business_date, symbol, stock_name,
                            sector_name, previous_close, pct_1d, pct_5d,
                            pct_20d, marketcap, pe_ratio, avg_turnover_30d)
                           VALUES
                           (@RunId, @BusinessDate, @Symbol, @StockName,
                            @SectorName, @PreviousClose, @Pct1d, @Pct5d,
                            @Pct20d, @MarketCap, @PeRatio, @AvgTurnover30d)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@RunId", runId);
                cmd.Parameters.AddWithValue("@BusinessDate", businessDate);
                cmd.Parameters.AddWithValue("@Symbol", stock.Symbol);
                cmd.Parameters.AddWithValue("@StockName", stock.StockName);
                cmd.Parameters.AddWithValue("@SectorName", stock.SectorName);
                cmd.Parameters.AddWithValue("@PreviousClose", stock.PreviousClose);
                cmd.Parameters.AddWithValue("@Pct1d", stock.Pct1d);
                cmd.Parameters.AddWithValue("@Pct5d", stock.Pct5d);
                cmd.Parameters.AddWithValue("@Pct20d", stock.Pct20d);
                cmd.Parameters.AddWithValue("@MarketCap", stock.MarketCap);
                cmd.Parameters.AddWithValue("@PeRatio", stock.PeRatio);
                cmd.Parameters.AddWithValue("@AvgTurnover30d", stock.AvgTurnover30d);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        // =============================================
        // FORBIDDEN SECTORS — Read
        // =============================================
        public async Task<List<string>> GetForbiddenSectorsAsync()
        {
            var result = new List<string>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = "SELECT sector_name FROM Forbidden_Sectors";
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                result.Add(reader["sector_name"].ToString()?.ToLower() ?? "");

            return result;
        }

        // =============================================
        // GENERIC QUERY — For testing
        // =============================================
        public async Task<int> GetRowCountAsync(string tableName)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $"SELECT COUNT(*) FROM {tableName}";
            using var cmd = new SqlCommand(sql, conn);
            return (int)await cmd.ExecuteScalarAsync();
        }

        // =============================================
        // ENSURE TABLE EXISTS — Auto create if missing
        // =============================================
        public async Task EnsureTableExistsAsync(string tableName)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = $@"
        IF NOT EXISTS (
            SELECT * FROM sysobjects
            WHERE name='{tableName}' AND xtype='U'
        )
        CREATE TABLE {tableName} (
            id            INT IDENTITY(1,1) PRIMARY KEY,
            run_id        NVARCHAR(50),
            business_date DATE,
            sector_name   NVARCHAR(100),
            pct_change    DECIMAL(10,4),
            rank          INT,
            created_at    DATETIME DEFAULT GETDATE()
        )";

            using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // =============================================
        // LOAD SECTORS FROM DB — Skip API call
        // =============================================
        public async Task<List<Sector>> LoadSectorsFromDbAsync(string tableName)
        {
            var result = new List<Sector>();

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get most recent business date in table
                var dateSql = $@"
            SELECT MAX(business_date)
            FROM {tableName}";

                using var dateCmd = new SqlCommand(dateSql, conn);
                var latestDate = await dateCmd.ExecuteScalarAsync();

                if (latestDate == null || latestDate == DBNull.Value)
                {
                    Console.WriteLine($"   ⚠️ No data found in {tableName}");
                    return result;
                }

                Console.WriteLine($"   → Loading from {tableName} (date: {latestDate})...");

                var sql = $@"
            SELECT sector_name, pct_change, rank
            FROM {tableName}
            WHERE business_date = @date
            ORDER BY rank ASC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", latestDate);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new Sector
                    {
                        SectorName = reader.GetString(0),
                        PctChange = reader.GetDecimal(1),
                        Pct1d = reader.GetDecimal(1),
                        Rank = reader.GetInt32(2)
                    });
                }

                Console.WriteLine($"   ✅ Loaded {result.Count} sectors from {tableName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error loading from {tableName}: {ex.Message}");
            }

            return result;
        }

        // =============================================
        // INSERT INTERSECTION RESULT
        // =============================================
        public async Task InsertIntersectionResultAsync(
            string runId,
            DateTime businessDate,
            List<string> selectedSectors,
            List<SectorScore> scores)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Ensure table exists
            var createSql = @"
    IF EXISTS (
        SELECT * FROM sysobjects
        WHERE name='Table_3_Sector_Intersection' AND xtype='U'
    )
    DROP TABLE Table_3_Sector_Intersection;

    CREATE TABLE Table_3_Sector_Intersection (
        id             INT IDENTITY(1,1) PRIMARY KEY,
        run_id         NVARCHAR(50),
        business_date  DATE,
        sector_name    NVARCHAR(100),
        us_pct         DECIMAL(10,4),
        india_pct      DECIMAL(10,4),
        china_pct      DECIMAL(10,4),
        combined_score DECIMAL(10,4),
        is_selected    BIT,
        rank           INT,
        created_at     DATETIME DEFAULT GETDATE()
    )";

            using (var cmd = new SqlCommand(createSql, conn))
                await cmd.ExecuteNonQueryAsync();

            // Insert each scored sector
            int rank = 1;
            foreach (var score in scores)
            {
                var isSelected = selectedSectors.Contains(
                    score.SectorName,
                    StringComparer.OrdinalIgnoreCase) ? 1 : 0;

                var sql = @"
            INSERT INTO Table_3_Sector_Intersection
                (run_id, business_date, sector_name,
                 us_pct, india_pct, china_pct,
                 combined_score, is_selected, rank)
            VALUES
                (@runId, @businessDate, @sectorName,
                 @usPct, @indiaPct, @chinaPct,
                 @combinedScore, @isSelected, @rank)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@businessDate", businessDate);
                cmd.Parameters.AddWithValue("@sectorName", score.SectorName);
                cmd.Parameters.AddWithValue("@usPct", score.USPct);
                cmd.Parameters.AddWithValue("@indiaPct", score.IndiaPct);
                cmd.Parameters.AddWithValue("@chinaPct", score.ChinaPct);
                cmd.Parameters.AddWithValue("@combinedScore", score.CombinedScore);
                cmd.Parameters.AddWithValue("@isSelected", isSelected);
                cmd.Parameters.AddWithValue("@rank", rank++);

                await cmd.ExecuteNonQueryAsync();
            }
        }
        // =============================================
        // INSERT TOP STOCKS
        // =============================================
        public async Task InsertTopStocksAsync(
            string runId,
            DateTime businessDate,
            List<Stock> stocks)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var createSql = @"
        IF NOT EXISTS (
            SELECT * FROM sysobjects
            WHERE name='Table_4_Top_Stocks' AND xtype='U'
        )
        CREATE TABLE Table_4_Top_Stocks (
            id             INT IDENTITY(1,1) PRIMARY KEY,
            run_id         NVARCHAR(50),
            business_date  DATE,
            symbol         NVARCHAR(20),
            stock_name     NVARCHAR(100),
            sector_name    NVARCHAR(100),
            pct_1d         DECIMAL(10,4),
            pe_ratio       DECIMAL(10,4),
            avg_turnover   DECIMAL(20,2),
            score          DECIMAL(10,6),
            rank           INT,
            created_at     DATETIME DEFAULT GETDATE()
        )";

            using (var cmd = new SqlCommand(createSql, conn))
                await cmd.ExecuteNonQueryAsync();

            foreach (var stock in stocks)
            {
                var sql = @"
            INSERT INTO Table_4_Top_Stocks
                (run_id, business_date, symbol, stock_name,
                 sector_name, pct_1d, pe_ratio, avg_turnover, score, rank)
            VALUES
                (@runId, @businessDate, @symbol, @stockName,
                 @sectorName, @pct1d, @peRatio, @avgTurnover, @score, @rank)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@businessDate", businessDate);
                cmd.Parameters.AddWithValue("@symbol", stock.Symbol);
                cmd.Parameters.AddWithValue("@stockName", stock.StockName);
                cmd.Parameters.AddWithValue("@sectorName", stock.SectorName);
                cmd.Parameters.AddWithValue("@pct1d", stock.Pct1d);
                cmd.Parameters.AddWithValue("@peRatio", stock.PERatio);
                cmd.Parameters.AddWithValue("@avgTurnover", stock.AvgTurnover30d);
                cmd.Parameters.AddWithValue("@score", stock.Score);
                cmd.Parameters.AddWithValue("@rank", stock.Rank);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
