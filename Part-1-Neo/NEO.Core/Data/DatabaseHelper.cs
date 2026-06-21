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
        // SMART CACHE — Check if today's data exists
        // =============================================
        public async Task<bool> HasTodaysDataAsync(string tableName, DateTime businessDate)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = $@"
            SELECT COUNT(*)
            FROM {tableName}
            WHERE business_date = @date";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", businessDate.Date);

                var count = (int)await cmd.ExecuteScalarAsync();
                return count > 0;
            }
            catch
            {
                // Table doesn't exist yet → no data
                return false;
            }
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
                cmd.Parameters.AddWithValue("@PeRatio", stock.PERatio);
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

            // Add columns if they don't exist yet
            var alterSql = @"
        IF NOT EXISTS (
            SELECT * FROM sys.columns
            WHERE object_id = OBJECT_ID('Table_4_Top_Stocks')
            AND name = 'previous_close')
        ALTER TABLE Table_4_Top_Stocks ADD previous_close DECIMAL(18,4) DEFAULT 0;

        IF NOT EXISTS (
            SELECT * FROM sys.columns
            WHERE object_id = OBJECT_ID('Table_4_Top_Stocks')
            AND name = 'pct_5d')
        ALTER TABLE Table_4_Top_Stocks ADD pct_5d DECIMAL(10,4) DEFAULT 0;";

            using (var cmd = new SqlCommand(alterSql, conn))
                await cmd.ExecuteNonQueryAsync();

            // Delete today's existing data
            var deleteSql = @"DELETE FROM Table_4_Top_Stocks WHERE business_date = @date";
            using (var cmd = new SqlCommand(deleteSql, conn))
            {
                cmd.Parameters.AddWithValue("@date", businessDate.Date);
                await cmd.ExecuteNonQueryAsync();
            }

            // Insert fresh data including previous_close and pct_5d
            foreach (var s in stocks)
            {
                var sql = @"
            INSERT INTO Table_4_Top_Stocks
                (run_id, business_date, symbol, stock_name,
                 sector_name, rank, score, pct_1d,
                 previous_close, pct_5d)
            VALUES
                (@runId, @businessDate, @symbol, @stockName,
                 @sectorName, @rank, @score, @pct1d,
                 @previousClose, @pct5d)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@businessDate", businessDate.Date);
                cmd.Parameters.AddWithValue("@symbol", s.Symbol);
                cmd.Parameters.AddWithValue("@stockName", s.StockName);
                cmd.Parameters.AddWithValue("@sectorName", s.SectorName);
                cmd.Parameters.AddWithValue("@rank", s.Rank);
                cmd.Parameters.AddWithValue("@score", s.Score);
                cmd.Parameters.AddWithValue("@pct1d", s.Pct1d);
                cmd.Parameters.AddWithValue("@previousClose", s.PreviousClose > 0 ? s.PreviousClose : s.Price);
                cmd.Parameters.AddWithValue("@pct5d", s.Pct5d);
                await cmd.ExecuteNonQueryAsync();
            }
        }


        public async Task InsertTradeSignalsAsync(
    string runId,
    DateTime businessDate,
    List<TradeSignal> signals)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Create table if not exists
            var createSql = @"
        IF NOT EXISTS (
            SELECT * FROM sysobjects
            WHERE name='Table_5_Trade_Signals' AND xtype='U'
        )
        CREATE TABLE Table_5_Trade_Signals (
            id            INT IDENTITY(1,1) PRIMARY KEY,
            run_id        NVARCHAR(50),
            business_date DATE,
            symbol        NVARCHAR(20),
            stock_name    NVARCHAR(100),
            sector_name   NVARCHAR(100),
            rank          INT,
            score         DECIMAL(10,6),
            pct_1d        DECIMAL(10,4),
            signal        NVARCHAR(10),
            reason        NVARCHAR(500),
            created_at    DATETIME DEFAULT GETDATE()
        )";

            using (var cmd = new SqlCommand(createSql, conn))
                await cmd.ExecuteNonQueryAsync();

            // Delete today's existing data first → prevent duplicates
            var deleteSql = @"DELETE FROM Table_5_Trade_Signals
                      WHERE business_date = @date";
            using (var cmd = new SqlCommand(deleteSql, conn))
            {
                cmd.Parameters.AddWithValue("@date", businessDate.Date);
                await cmd.ExecuteNonQueryAsync();
            }

            // Insert fresh data
            foreach (var s in signals)
            {
                var sql = @"
            INSERT INTO Table_5_Trade_Signals
                (run_id, business_date, symbol, stock_name,
                 sector_name, rank, score, pct_1d, signal, reason)
            VALUES
                (@runId, @businessDate, @symbol, @stockName,
                 @sectorName, @rank, @score, @pct1d, @signal, @reason)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@businessDate", businessDate.Date);
                cmd.Parameters.AddWithValue("@symbol", s.Symbol);
                cmd.Parameters.AddWithValue("@stockName", s.StockName);
                cmd.Parameters.AddWithValue("@sectorName", s.SectorName);
                cmd.Parameters.AddWithValue("@rank", s.Rank);
                cmd.Parameters.AddWithValue("@score", s.Score);
                cmd.Parameters.AddWithValue("@pct1d", s.Pct1d);
                cmd.Parameters.AddWithValue("@signal", s.Signal);
                cmd.Parameters.AddWithValue("@reason", s.Reason);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        // =============================================
        // LOAD TOP STOCKS FROM DB CACHE
        // =============================================
        public async Task<List<Stock>> LoadTopStocksAsync(DateTime businessDate)
        {
            var result = new List<Stock>();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"SELECT symbol, stock_name, sector_name,
                           rank, score, pct_1d,
                           ISNULL(previous_close, 0) AS previous_close,
                           ISNULL(pct_5d, 0)         AS pct_5d
                    FROM   Table_4_Top_Stocks
                    WHERE  business_date = @date
                    ORDER  BY rank ASC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", businessDate.Date);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new Stock
                    {
                        Symbol = reader["symbol"].ToString() ?? "",
                        StockName = reader["stock_name"].ToString() ?? "",
                        SectorName = reader["sector_name"].ToString() ?? "",
                        Rank = Convert.ToInt32(reader["rank"]),
                        Score = Convert.ToDecimal(reader["score"]),
                        Pct1d = Convert.ToDecimal(reader["pct_1d"]),
                        PreviousClose = Convert.ToDecimal(reader["previous_close"]),
                        Price = Convert.ToDecimal(reader["previous_close"]),
                        Pct5d = Convert.ToDecimal(reader["pct_5d"])
                    });
                }
            }
            catch { }
            return result;
        }


        // =============================================
        // LOAD TODAY'S SIGNALS
        // =============================================
        public async Task<List<TradeSignal>> LoadTodaySignalsAsync()
        {
            var result = new List<TradeSignal>();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"SELECT TOP 50
                        run_id, business_date, symbol, stock_name,
                        sector_name, rank, score, pct_1d, signal, reason
                    FROM  Table_5_Trade_Signals
                    WHERE business_date = CAST(GETDATE() AS DATE)
                    ORDER BY rank ASC";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    result.Add(new TradeSignal
                    {
                        RunId = reader["run_id"].ToString() ?? "",
                        BusinessDate = Convert.ToDateTime(reader["business_date"]),
                        Symbol = reader["symbol"].ToString() ?? "",
                        StockName = reader["stock_name"].ToString() ?? "",
                        SectorName = reader["sector_name"].ToString() ?? "",
                        Rank = Convert.ToInt32(reader["rank"]),
                        Score = Convert.ToDecimal(reader["score"]),
                        Pct1d = Convert.ToDecimal(reader["pct_1d"]),
                        Signal = reader["signal"].ToString() ?? "",
                        Reason = reader["reason"].ToString() ?? ""
                    });
                }
            }
            catch { }
            return result;
        }

        // =============================================
        // LOAD TODAY'S SECTORS
        // =============================================
        public async Task<List<Sector>> LoadTodaySectorsAsync(string market)
        {
            var result = new List<Sector>();
            var table = market.ToUpper() switch
            {
                "US" => "Table_2_Sector_1D_US",
                "INDIA" => "Table_2_Sector_1D_India",
                "CHINA" => "Table_2_Sector_1D_China",
                _ => ""
            };

            if (string.IsNullOrEmpty(table)) return result;

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = $@"SELECT sector_name, pct_change, rank
                     FROM   {table}
                     WHERE  business_date = CAST(GETDATE() AS DATE)
                     ORDER  BY rank ASC";

                using var cmd = new SqlCommand(sql, conn);
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
            }
            catch { }
            return result;
        }

        // =============================================
        // LOAD SELECTED SECTORS
        // =============================================
        public async Task<List<string>> LoadSelectedSectorsAsync()
        {
            var result = new List<string>();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"SELECT DISTINCT sector_name
                    FROM   Table_5_Trade_Signals
                    WHERE  business_date = CAST(GETDATE() AS DATE)";

                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                    result.Add(reader["sector_name"].ToString() ?? "");
            }
            catch { }
            return result;
        }

        // =============================================
        // LOAD RUN HISTORY
        // =============================================
        public async Task<List<RunLog>> LoadRunHistoryAsync(int days = 30)
        {
            var result = new List<RunLog>();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"SELECT run_id, business_date, stage, message,
                           status, created_at
                    FROM   Run_Decision_Log
                    WHERE  stage IN ('PIPELINE','STAGE_8','STAGE_9')
                    AND    created_at >= DATEADD(DAY, -@days, GETDATE())
                    ORDER  BY created_at DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@days", days);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new RunLog
                    {
                        RunId = reader["run_id"].ToString() ?? "",
                        BusinessDate = Convert.ToDateTime(reader["business_date"]),
                        Stage = reader["stage"].ToString() ?? "",
                        Message = reader["message"].ToString() ?? "",
                        Status = reader["status"].ToString() ?? "",
                        CreatedAt = Convert.ToDateTime(reader["created_at"])
                    });
                }
            }
            catch { }
            return result;
        }

        // =============================================
        // LOAD SIGNALS BY DATE
        // =============================================
        public async Task<List<TradeSignal>> LoadSignalsByDateAsync(DateTime date)
        {
            var result = new List<TradeSignal>();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var sql = @"SELECT run_id, business_date, symbol, stock_name,
                           sector_name, rank, score, pct_1d, signal, reason
                    FROM   Table_5_Trade_Signals
                    WHERE  business_date = @date
                    ORDER  BY rank ASC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", date.Date);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new TradeSignal
                    {
                        RunId = reader["run_id"].ToString() ?? "",
                        BusinessDate = Convert.ToDateTime(reader["business_date"]),
                        Symbol = reader["symbol"].ToString() ?? "",
                        StockName = reader["stock_name"].ToString() ?? "",
                        SectorName = reader["sector_name"].ToString() ?? "",
                        Rank = Convert.ToInt32(reader["rank"]),
                        Score = Convert.ToDecimal(reader["score"]),
                        Pct1d = Convert.ToDecimal(reader["pct_1d"]),
                        Signal = reader["signal"].ToString() ?? "",
                        Reason = reader["reason"].ToString() ?? ""
                    });
                }
            }
            catch { }
            return result;
        }

        //public async Task<List<Stock>> LoadStocksBySectorAsync(string sectorName)
        //{
        //    var result = new List<Stock>();

        //    using var conn = new SqlConnection(_connectionString);
        //    await conn.OpenAsync();

        //    var sql = @"
        //SELECT symbol, stock_name, sector_name,
        //       previous_close, pct_1d, pct_5d, pct_20d,
        //       marketcap, pe_ratio, avg_turnover_30d
        //FROM Table_1_All_Stocks
        //WHERE sector_name = @SectorName
        //  AND business_date = CAST(GETDATE() AS DATE)";

        //    using var cmd = new SqlCommand(sql, conn);
        //    cmd.Parameters.AddWithValue("@SectorName", sectorName);

        //    using var reader = await cmd.ExecuteReaderAsync();

        //    while (await reader.ReadAsync())
        //    {
        //        result.Add(new Stock
        //        {
        //            Symbol = reader["symbol"].ToString() ?? "",
        //            StockName = reader["stock_name"].ToString() ?? "",
        //            SectorName = reader["sector_name"].ToString() ?? "",
        //            PreviousClose = Convert.ToDecimal(reader["previous_close"]),
        //            Pct1d = Convert.ToDecimal(reader["pct_1d"]),
        //            Pct5d = Convert.ToDecimal(reader["pct_5d"]),
        //            Pct20d = Convert.ToDecimal(reader["pct_20d"]),
        //            MarketCap = Convert.ToDecimal(reader["marketcap"]),
        //            PERatio = Convert.ToDecimal(reader["pe_ratio"]),
        //            AvgTurnover30d = Convert.ToDecimal(reader["avg_turnover_30d"])
        //        });
        //    }

        //    return result;
        //}

        public async Task<List<Stock>> LoadStocksBySectorAsync(string sectorName)
        {
            var result = new List<Stock>();

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                // Get latest business date available in Table_1_All_Stocks
                var dateSql = @"
            SELECT MAX(business_date)
            FROM Table_1_All_Stocks";

                using var dateCmd = new SqlCommand(dateSql, conn);
                var latestDate = await dateCmd.ExecuteScalarAsync();

                if (latestDate == null || latestDate == DBNull.Value)
                {
                    Console.WriteLine("   ⚠️ No stock data found in Table_1_All_Stocks");
                    return result;
                }

                var sql = @"
            SELECT symbol,
                   stock_name,
                   sector_name,
                   ISNULL(previous_close, 0) AS previous_close,
                   ISNULL(pct_1d, 0)         AS pct_1d,
                   ISNULL(pct_5d, 0)         AS pct_5d,
                   ISNULL(pct_20d, 0)        AS pct_20d,
                   ISNULL(marketcap, 0)      AS marketcap,
                   ISNULL(pe_ratio, 0)       AS pe_ratio,
                   ISNULL(avg_turnover_30d, 0) AS avg_turnover_30d
            FROM Table_1_All_Stocks
            WHERE business_date = @date
              AND LOWER(LTRIM(RTRIM(sector_name))) = LOWER(LTRIM(RTRIM(@sectorName)))
            ORDER BY pct_1d DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", Convert.ToDateTime(latestDate).Date);
                cmd.Parameters.AddWithValue("@sectorName", sectorName);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new Stock
                    {
                        Symbol = reader["symbol"].ToString() ?? "",
                        StockName = reader["stock_name"].ToString() ?? "",
                        SectorName = reader["sector_name"].ToString() ?? "",
                        PreviousClose = Convert.ToDecimal(reader["previous_close"]),
                        Price = Convert.ToDecimal(reader["previous_close"]),
                        Pct1d = Convert.ToDecimal(reader["pct_1d"]),
                        Pct5d = Convert.ToDecimal(reader["pct_5d"]),
                        Pct20d = Convert.ToDecimal(reader["pct_20d"]),
                        MarketCap = Convert.ToDecimal(reader["marketcap"]),
                        PERatio = Convert.ToDecimal(reader["pe_ratio"]),
                        AvgTurnover30d = Convert.ToDecimal(reader["avg_turnover_30d"])
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error loading stocks for sector '{sectorName}': {ex.Message}");
            }

            return result;
        }



    }
}
