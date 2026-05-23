using Microsoft.Data.SqlClient;
using NEO.Core.Models;
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
    }
}
