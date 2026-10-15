using ChurchYouTubeAssistant.Models;

namespace ChurchYouTubeAssistant.Services;

/// <summary>One of the 20 fixed sermon-classification categories, with its Russian and English display names.</summary>
public sealed record TheologyCategoryInfo(TheologyCategory Category, string RussianName, string EnglishName);

/// <summary>
/// Display names for <see cref="TheologyCategory"/> and the matcher that turns the AI's output
/// string back into the enum.
/// </summary>
/// <remarks>
/// Unlike <see cref="BibleBookCatalog"/>, the AI is instructed (see Prompts/VideoAnalysisV11) to
/// always output the English label verbatim regardless of the sermon's language - this taxonomy is
/// a closed classification, not free text, so there is no need to recognize a Russian-language
/// answer the way Bible references require.
/// </remarks>
public static class TheologyCategoryCatalog
{
    public static IReadOnlyList<TheologyCategoryInfo> All { get; } =
    [
        new(TheologyCategory.Bibliology, "Библиология — учение о Священном Писании", "Bibliology"),
        new(TheologyCategory.TheologyProper, "Теология — учение о Боге", "Theology Proper"),
        new(TheologyCategory.TrinitarianTheology, "Тринитарное богословие — учение о Троице", "Trinitarian Theology"),
        new(TheologyCategory.Christology, "Христология — учение об Иисусе Христе", "Christology"),
        new(TheologyCategory.Pneumatology, "Пневматология — учение о Святом Духе", "Pneumatology"),
        new(TheologyCategory.Angelology, "Ангелология — учение об ангелах", "Angelology"),
        new(TheologyCategory.Demonology, "Демонология — учение о сатане и демонах", "Demonology"),
        new(TheologyCategory.TheologicalAnthropology, "Антропология — учение о человеке", "Theological Anthropology"),
        new(TheologyCategory.Hamartiology, "Хамартиология — учение о грехе", "Hamartiology"),
        new(TheologyCategory.Soteriology, "Сотериология — учение о спасении", "Soteriology"),
        new(TheologyCategory.Ecclesiology, "Экклезиология — учение о Церкви", "Ecclesiology"),
        new(TheologyCategory.Eschatology, "Эсхатология — учение о последних временах", "Eschatology"),
        new(TheologyCategory.ChristianEthics, "Христианская этика", "Christian Ethics"),
        new(TheologyCategory.SpiritualFormationAndSanctification, "Духовная жизнь и освящение", "Spiritual Formation and Sanctification"),
        new(TheologyCategory.Discipleship, "Ученичество и следование за Христом", "Discipleship"),
        new(TheologyCategory.MissiologyAndEvangelism, "Миссиология и благовестие", "Missiology and Evangelism"),
        new(TheologyCategory.Apologetics, "Апологетика — защита веры", "Apologetics"),
        new(TheologyCategory.MarriageFamilyAndParenting, "Брак, семья и воспитание", "Marriage, Family and Parenting"),
        new(TheologyCategory.WorshipAndPrayer, "Поклонение и молитва", "Worship and Prayer"),
        new(TheologyCategory.BiblicalHermeneutics, "Библейская герменевтика", "Biblical Hermeneutics")
    ];

    private static readonly Dictionary<string, TheologyCategory> ByEnglishName =
        All.ToDictionary(c => Normalize(c.EnglishName), c => c.Category);

    public static string EnglishNameOf(TheologyCategory category) =>
        All.First(c => c.Category == category).EnglishName;

    public static string RussianNameOf(TheologyCategory category) =>
        All.First(c => c.Category == category).RussianName;

    /// <summary>Matches the AI's raw output string (expected to be one of the English labels) back to the enum.</summary>
    public static TheologyCategory? TryParse(string? rawCategory)
    {
        if (string.IsNullOrWhiteSpace(rawCategory))
        {
            return null;
        }

        return ByEnglishName.TryGetValue(Normalize(rawCategory), out var category) ? category : null;
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
