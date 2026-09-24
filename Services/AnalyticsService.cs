using Microsoft.EntityFrameworkCore;
using UTHMBargain.Data;
using UTHMBargain.Models;

namespace UTHMBargain.Services;

public class AnalyticsService
{
    private readonly ApplicationDbContext _db;

    public AnalyticsService(ApplicationDbContext db) { _db = db; }

    // ==================== ADMIN ANALYTICS ====================

    public async Task<AdminAnalytics> GetAdminAnalyticsAsync(int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-days);

        // Completed transactions in range
        var completed = await _db.EscrowTransactions
            .Include(e => e.Product).ThenInclude(p => p!.Category)
            .Include(e => e.Seller).Include(e => e.Buyer)
            .Where(e => e.Status == EscrowStatus.Completed && e.CompletedAt >= since)
            .ToListAsync();

        // All transactions ever (for lifetime stats)
        var allCompleted = await _db.EscrowTransactions
            .Where(e => e.Status == EscrowStatus.Completed)
            .ToListAsync();

        // Daily revenue series
        var daily = completed
            .GroupBy(e => e.CompletedAt!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyPoint
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(x => x.Amount)
            })
            .ToList();

        // Fill missing days with zero
        var fullRange = new List<DailyPoint>();
        for (int i = days - 1; i >= 0; i--)
        {
            var d = DateTime.UtcNow.Date.AddDays(-i);
            var found = daily.FirstOrDefault(x => x.Date == d);
            fullRange.Add(found ?? new DailyPoint { Date = d, Count = 0, Revenue = 0m });
        }

        // Top categories
        var topCategories = completed
            .GroupBy(e => e.Product!.Category!.Name)
            .Select(g => new CategoryStat
            {
                Name = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToList();

        // Top products
        var topProducts = completed
            .GroupBy(e => e.Product!.Title)
            .Select(g => new ProductStat
            {
                Title = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToList();

        // Top sellers
        var topSellers = completed
            .GroupBy(e => e.Seller!.FullName)
            .Select(g => new UserStat
            {
                Name = g.Key,
                Count = g.Count(),
                Amount = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Amount)
            .Take(5)
            .ToList();

        // Top buyers
        var topBuyers = completed
            .GroupBy(e => e.Buyer!.FullName)
            .Select(g => new UserStat
            {
                Name = g.Key,
                Count = g.Count(),
                Amount = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Amount)
            .Take(5)
            .ToList();

        // ===== AI: Linear regression forecast for next 7 days =====
        var forecast = ForecastNextDays(fullRange, 7);

        // ===== AI: Anomaly detection =====
        var anomalies = DetectAnomalies(fullRange);

        // ===== AI: Text insights =====
        var insights = GenerateAdminInsights(fullRange, topCategories, completed.Count, allCompleted.Sum(e => e.Amount));

        // Growth vs previous period
        var prevSince = since.AddDays(-days);
        var prevRevenue = allCompleted
            .Where(e => e.CompletedAt >= prevSince && e.CompletedAt < since)
            .Sum(e => e.Amount);
        var currentRevenue = completed.Sum(e => e.Amount);
        decimal growthPct = prevRevenue == 0
            ? (currentRevenue > 0 ? 100 : 0)
            : Math.Round((currentRevenue - prevRevenue) / prevRevenue * 100, 1);

        return new AdminAnalytics
        {
            RangeDays = days,
            TotalRevenue = currentRevenue,
            TotalTransactions = completed.Count,
            LifetimeRevenue = allCompleted.Sum(e => e.Amount),
            LifetimeTransactions = allCompleted.Count,
            ActiveEscrows = await _db.EscrowTransactions.CountAsync(e =>
                e.Status == EscrowStatus.Pending || e.Status == EscrowStatus.Shipped),
            OpenDisputes = await _db.EscrowTransactions.CountAsync(e => e.Status == EscrowStatus.Disputed),
            TotalUsers = await _db.Users.CountAsync(),
            TotalListings = await _db.Products.CountAsync(),
            AvgOrderValue = completed.Any() ? Math.Round(completed.Average(e => e.Amount), 2) : 0m,
            GrowthPercent = growthPct,
            DailySeries = fullRange,
            Forecast = forecast,
            Anomalies = anomalies,
            TopCategories = topCategories,
            TopProducts = topProducts,
            TopSellers = topSellers,
            TopBuyers = topBuyers,
            Insights = insights
        };
    }

    // ==================== USER PERSONAL ANALYTICS ====================

    public async Task<UserAnalytics> GetUserAnalyticsAsync(string userId)
    {
        var purchases = await _db.EscrowTransactions
            .Include(e => e.Product).ThenInclude(p => p!.Category)
            .Include(e => e.Seller)
            .Where(e => e.BuyerId == userId && e.Status == EscrowStatus.Completed)
            .ToListAsync();

        var sales = await _db.EscrowTransactions
            .Include(e => e.Product).ThenInclude(p => p!.Category)
            .Include(e => e.Buyer)
            .Where(e => e.SellerId == userId && e.Status == EscrowStatus.Completed)
            .ToListAsync();

        var ratings = await _db.Ratings
            .Where(r => r.RatedUserId == userId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        // Monthly spend/sell series (last 6 months)
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var monthKeys = Enumerable.Range(0, 6)
            .Select(i => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-i))
            .OrderBy(d => d)
            .ToList();

        var monthly = monthKeys.Select(m =>
        {
            var nextMonth = m.AddMonths(1);
            return new MonthlyPoint
            {
                Month = m.ToString("MMM yyyy"),
                Spent = purchases.Where(p => p.CompletedAt >= m && p.CompletedAt < nextMonth).Sum(p => p.Amount),
                Earned = sales.Where(s => s.CompletedAt >= m && s.CompletedAt < nextMonth).Sum(s => s.Amount),
                Purchases = purchases.Count(p => p.CompletedAt >= m && p.CompletedAt < nextMonth),
                SalesCount = sales.Count(s => s.CompletedAt >= m && s.CompletedAt < nextMonth)
            };
        }).ToList();

        // Favorite categories
        var favCategories = purchases
            .GroupBy(p => p.Product!.Category!.Name)
            .Select(g => new CategoryStat
            {
                Name = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        // Rating trend (avg per month)
        var ratingTrend = ratings
            .GroupBy(r => new DateTime(r.CreatedAt.Year, r.CreatedAt.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new RatingPoint
            {
                Month = g.Key.ToString("MMM yyyy"),
                AvgStars = Math.Round(g.Average(x => x.Stars), 2),
                Count = g.Count()
            })
            .ToList();

        // AI insights
        var insights = GenerateUserInsights(userId, purchases, sales, ratings, favCategories);

        return new UserAnalytics
        {
            TotalSpent = purchases.Sum(p => p.Amount),
            TotalEarned = sales.Sum(s => s.Amount),
            PurchaseCount = purchases.Count,
            SalesCount = sales.Count,
            AvgStarsReceived = ratings.Any() ? Math.Round(ratings.Average(r => r.Stars), 2) : 0,
            AvgStarsGiven = await _db.Ratings.Where(r => r.RaterId == userId).AverageAsync(r => (double?)r.Stars) ?? 0,
            Monthly = monthly,
            FavoriteCategories = favCategories,
            RatingTrend = ratingTrend,
            Insights = insights
        };
    }

    // ==================== AI HELPERS ====================

    /// <summary>
    /// Simple linear regression (least squares) to forecast future values.
    /// </summary>
    private static List<ForecastPoint> ForecastNextDays(List<DailyPoint> history, int daysAhead)
    {
        var result = new List<ForecastPoint>();
        if (history.Count < 2) return result;

        // x = day index (0..n-1), y = revenue
        var n = history.Count;
        var xs = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var ys = history.Select(h => (double)h.Revenue).ToArray();

        var sumX = xs.Sum();
        var sumY = ys.Sum();
        var sumXY = xs.Zip(ys, (x, y) => x * y).Sum();
        var sumX2 = xs.Select(x => x * x).Sum();

        var denom = n * sumX2 - sumX * sumX;
        if (denom == 0) return result;

        var slope = (n * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / n;

        // std dev for confidence band
        var predicted = xs.Select(x => slope * x + intercept).ToArray();
        var variance = ys.Zip(predicted, (y, p) => Math.Pow(y - p, 2)).Sum() / n;
        var stdDev = Math.Sqrt(variance);

        var lastDate = history.Last().Date;
        for (int i = 1; i <= daysAhead; i++)
        {
            var x = n + i - 1;
            var predictedVal = slope * x + intercept;
            if (predictedVal < 0) predictedVal = 0;

            result.Add(new ForecastPoint
            {
                Date = lastDate.AddDays(i),
                Predicted = Math.Round((decimal)predictedVal, 2),
                LowerBound = Math.Round((decimal)Math.Max(0, predictedVal - stdDev), 2),
                UpperBound = Math.Round((decimal)(predictedVal + stdDev), 2)
            });
        }

        return result;
    }

    /// <summary>
    /// Detects days where revenue is > 2 standard deviations from mean.
    /// </summary>
    private static List<Anomaly> DetectAnomalies(List<DailyPoint> series)
    {
        var result = new List<Anomaly>();
        if (series.Count < 5) return result;

        var mean = (double)series.Average(p => p.Revenue);
        var variance = series.Sum(p => Math.Pow((double)p.Revenue - mean, 2)) / series.Count;
        var stdDev = Math.Sqrt(variance);
        if (stdDev < 0.01) return result;

        foreach (var p in series)
        {
            var z = ((double)p.Revenue - mean) / stdDev;
            if (Math.Abs(z) >= 2.0)
            {
                result.Add(new Anomaly
                {
                    Date = p.Date,
                    Value = p.Revenue,
                    ZScore = Math.Round(z, 2),
                    Type = z > 0 ? "Spike" : "Drop",
                    Message = z > 0
                        ? $"Unusually high revenue (RM {p.Revenue:F2}) on {p.Date:dd MMM}"
                        : $"Unusually low revenue (RM {p.Revenue:F2}) on {p.Date:dd MMM}"
                });
            }
        }
        return result.OrderByDescending(a => Math.Abs(a.ZScore)).Take(3).ToList();
    }

    private static List<string> GenerateAdminInsights(
        List<DailyPoint> series,
        List<CategoryStat> topCats,
        int txCount,
        decimal lifetime)
    {
        var insights = new List<string>();

        if (series.Any())
        {
            var last7 = series.TakeLast(7).Sum(p => p.Revenue);
            var prev7 = series.Skip(Math.Max(0, series.Count - 14)).Take(7).Sum(p => p.Revenue);
            if (prev7 > 0)
            {
                var change = Math.Round((last7 - prev7) / prev7 * 100, 1);
                if (change > 10)
                    insights.Add($"📈 Revenue is up **{change}%** in the last 7 days compared to the previous week.");
                else if (change < -10)
                    insights.Add($"📉 Revenue dropped **{Math.Abs(change)}%** this week. Consider promoting active listings.");
                else
                    insights.Add($"➡️ Revenue is stable (change: {change}%).");
            }
        }

        if (topCats.Any())
        {
            var top = topCats.First();
            insights.Add($"🥇 **{top.Name}** is your top category with RM {top.Revenue:F2} from {top.Count} sales.");
        }

        if (txCount > 0)
            insights.Add($"🛒 {txCount} transactions completed this period.");
        else
            insights.Add($"🛒 No transactions yet this period. Consider boosting new listings.");

        insights.Add($"💼 Lifetime revenue: RM {lifetime:F2}.");

        return insights;
    }

    private static List<string> GenerateUserInsights(
        string userId,
        List<EscrowTransaction> purchases,
        List<EscrowTransaction> sales,
        List<Rating> ratings,
        List<CategoryStat> favCats)
    {
        var insights = new List<string>();

        var totalSpent = purchases.Sum(p => p.Amount);
        var totalEarned = sales.Sum(s => s.Amount);

        if (purchases.Any())
            insights.Add($"🛍️ You've bought **{purchases.Count}** items for a total of RM {totalSpent:F2}.");
        else
            insights.Add($"🛍️ You haven't made any purchases yet. Browse listings to start!");

        if (sales.Any())
            insights.Add($"💰 You've sold **{sales.Count}** items earning RM {totalEarned:F2}.");
        else
            insights.Add($"💡 You haven't sold anything yet. Try listing an item you no longer need.");

        if (favCats.Any())
        {
            var top = favCats.First();
            insights.Add($"❤️ Your favorite category is **{top.Name}** ({top.Count} purchases).");
        }

        if (ratings.Any())
        {
            var avg = ratings.Average(r => r.Stars);
            if (avg >= 4.5)
                insights.Add($"⭐ Outstanding! Your average rating is **{avg:F1}** — keep it up!");
            else if (avg >= 4.0)
                insights.Add($"⭐ Great rating of **{avg:F1}**. Buyers are happy with you.");
            else
                insights.Add($"⚠️ Your rating is **{avg:F1}**. Consider improving communication or descriptions.");
        }
        else
        {
            insights.Add($"💭 You haven't been rated yet. Complete a transaction to receive your first rating.");
        }

        var net = totalEarned - totalSpent;
        if (net > 0)
            insights.Add($"📊 Net profit: **RM {net:F2}** — you're in the green!");
        else if (net < 0)
            insights.Add($"📊 Net spending: RM {Math.Abs(net):F2} — you've spent more than you earned.");
        else
            insights.Add($"📊 You're breaking even.");

        return insights;
    }
}

// ==================== DTO MODELS ====================

public class AdminAnalytics
{
    public int RangeDays { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalTransactions { get; set; }
    public decimal LifetimeRevenue { get; set; }
    public int LifetimeTransactions { get; set; }
    public int ActiveEscrows { get; set; }
    public int OpenDisputes { get; set; }
    public int TotalUsers { get; set; }
    public int TotalListings { get; set; }
    public decimal AvgOrderValue { get; set; }
    public decimal GrowthPercent { get; set; }

    public List<DailyPoint> DailySeries { get; set; } = new();
    public List<ForecastPoint> Forecast { get; set; } = new();
    public List<Anomaly> Anomalies { get; set; } = new();
    public List<CategoryStat> TopCategories { get; set; } = new();
    public List<ProductStat> TopProducts { get; set; } = new();
    public List<UserStat> TopSellers { get; set; } = new();
    public List<UserStat> TopBuyers { get; set; } = new();
    public List<string> Insights { get; set; } = new();
}

public class UserAnalytics
{
    public decimal TotalSpent { get; set; }
    public decimal TotalEarned { get; set; }
    public int PurchaseCount { get; set; }
    public int SalesCount { get; set; }
    public double AvgStarsReceived { get; set; }
    public double AvgStarsGiven { get; set; }

    public List<MonthlyPoint> Monthly { get; set; } = new();
    public List<CategoryStat> FavoriteCategories { get; set; } = new();
    public List<RatingPoint> RatingTrend { get; set; } = new();
    public List<string> Insights { get; set; } = new();
}

public class DailyPoint
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}

public class MonthlyPoint
{
    public string Month { get; set; } = "";
    public decimal Spent { get; set; }
    public decimal Earned { get; set; }
    public int Purchases { get; set; }
    public int SalesCount { get; set; }
}

public class ForecastPoint
{
    public DateTime Date { get; set; }
    public decimal Predicted { get; set; }
    public decimal LowerBound { get; set; }
    public decimal UpperBound { get; set; }
}

public class Anomaly
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }
    public double ZScore { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}

public class CategoryStat
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}

public class ProductStat
{
    public string Title { get; set; } = "";
    public int Count { get; set; }
    public decimal Revenue { get; set; }
}

public class UserStat
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class RatingPoint
{
    public string Month { get; set; } = "";
    public double AvgStars { get; set; }
    public int Count { get; set; }
}