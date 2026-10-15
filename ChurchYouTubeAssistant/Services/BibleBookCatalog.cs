namespace ChurchYouTubeAssistant.Services;

public enum BibleTestament
{
    Old,
    New
}

/// <summary>One of the 66 canonical books, with every normalized prefix the parser will recognize.</summary>
public sealed record BibleBook(string CanonicalName, BibleTestament Testament, string[] MatchPrefixes);

/// <summary>
/// Matches a free-text Bible reference (as written by the AI analysis, e.g. "1 John 4:7-12" or
/// "Иоанна 3:16") to one of the 66 canonical books, for the sermon-analytics "Bible coverage" report.
/// </summary>
/// <remarks>
/// The analysis prompt (see Prompts/VideoAnalysisV*) explicitly tells the model to write references
/// "in the Bible book names and numbering convention natural to the transcript's language", so a
/// Russian-language sermon's references come back in Russian, not transliterated English. This
/// catalog therefore carries both English and Russian prefixes for every book. Matching is
/// deliberately a simple "does the normalized reference start with this prefix" check (not a strict
/// Bible-reference grammar parser) because sermon references are written in free-form prose, not a
/// fixed citation format - the goal is best-effort coverage for a dashboard, not a scripture API.
/// Russian 1-4 Царств maps to 1-2 Samuel / 1-2 Kings (the traditional Synodal Bible split), not a
/// literal "Kingdoms" book - that mapping looks surprising if you don't already know the Synodal
/// numbering convention.
/// </remarks>
public static class BibleBookCatalog
{
    public static IReadOnlyList<BibleBook> AllBooks { get; } =
    [
        // --- Old Testament (39) ---
        new("Genesis", BibleTestament.Old, ["genesis", "gen", "бытие", "бытия", "быт"]),
        new("Exodus", BibleTestament.Old, ["exodus", "exo", "исход", "исхода", "исх"]),
        new("Leviticus", BibleTestament.Old, ["leviticus", "lev", "левит", "левита", "лев"]),
        new("Numbers", BibleTestament.Old, ["numbers", "num", "числа", "чисел", "чис"]),
        new("Deuteronomy", BibleTestament.Old, ["deuteronomy", "deut", "второзаконие", "второзакония", "втор"]),
        new("Joshua", BibleTestament.Old, ["joshua", "josh", "иисус навин", "иисуса навина", "нав"]),
        new("Judges", BibleTestament.Old, ["judges", "judg", "судьи", "судей", "суд"]),
        new("Ruth", BibleTestament.Old, ["ruth", "руфь"]),
        new("1 Samuel", BibleTestament.Old,
            ["1 samuel", "1st samuel", "i samuel", "first samuel", "1-я царств", "1 царств", "первая книга царств", "первая царств"]),
        new("2 Samuel", BibleTestament.Old,
            ["2 samuel", "2nd samuel", "ii samuel", "second samuel", "2-я царств", "2 царств", "вторая книга царств", "вторая царств"]),
        new("1 Kings", BibleTestament.Old,
            ["1 kings", "1st kings", "i kings", "first kings", "3-я царств", "3 царств", "третья книга царств", "третья царств"]),
        new("2 Kings", BibleTestament.Old,
            ["2 kings", "2nd kings", "ii kings", "second kings", "4-я царств", "4 царств", "четвертая книга царств", "четвёртая книга царств"]),
        new("1 Chronicles", BibleTestament.Old,
            ["1 chronicles", "1st chronicles", "i chronicles", "first chronicles", "1-я паралипоменон", "1 паралипоменон", "первая паралипоменон"]),
        new("2 Chronicles", BibleTestament.Old,
            ["2 chronicles", "2nd chronicles", "ii chronicles", "second chronicles", "2-я паралипоменон", "2 паралипоменон", "вторая паралипоменон"]),
        new("Ezra", BibleTestament.Old, ["ezra", "ездра", "ездры", "езд"]),
        new("Nehemiah", BibleTestament.Old, ["nehemiah", "neh", "неемия", "неемии", "неем"]),
        new("Esther", BibleTestament.Old, ["esther", "есфирь", "есф"]),
        new("Job", BibleTestament.Old, ["job", "иов", "иова"]),
        new("Psalms", BibleTestament.Old, ["psalms", "psalm", "псалтирь", "псалом", "псалмы", "пс"]),
        new("Proverbs", BibleTestament.Old, ["proverbs", "prov", "притчи", "прит"]),
        new("Ecclesiastes", BibleTestament.Old, ["ecclesiastes", "eccl", "екклесиаст", "еккл"]),
        new("Song of Solomon", BibleTestament.Old,
            ["song of solomon", "song of songs", "песнь песней", "песни песней"]),
        new("Isaiah", BibleTestament.Old, ["isaiah", "isa", "исаия", "исаии", "исайя", "исайи", "ис"]),
        new("Jeremiah", BibleTestament.Old, ["jeremiah", "jer", "иеремия", "иеремии", "иер"]),
        new("Lamentations", BibleTestament.Old, ["lamentations", "lam", "плач иеремии", "плача иеремии", "плач"]),
        new("Ezekiel", BibleTestament.Old, ["ezekiel", "ezek", "иезекииль", "иезекииля", "иез"]),
        new("Daniel", BibleTestament.Old, ["daniel", "dan", "даниил", "даниила", "дан"]),
        new("Hosea", BibleTestament.Old, ["hosea", "hos", "осия", "осии", "ос"]),
        new("Joel", BibleTestament.Old, ["joel", "иоиль", "иоиля"]),
        new("Amos", BibleTestament.Old, ["amos", "амос", "амоса"]),
        new("Obadiah", BibleTestament.Old, ["obadiah", "авдий", "авдия"]),
        new("Jonah", BibleTestament.Old, ["jonah", "иона", "ионы"]),
        new("Micah", BibleTestament.Old, ["micah", "mic", "михей", "михея", "мих"]),
        new("Nahum", BibleTestament.Old, ["nahum", "наум", "наума"]),
        new("Habakkuk", BibleTestament.Old, ["habakkuk", "аввакум", "аввакума"]),
        new("Zephaniah", BibleTestament.Old, ["zephaniah", "софония", "софонии"]),
        new("Haggai", BibleTestament.Old, ["haggai", "аггей", "аггея"]),
        new("Zechariah", BibleTestament.Old, ["zechariah", "захария", "захарии"]),
        new("Malachi", BibleTestament.Old, ["malachi", "малахия", "малахии"]),

        // --- New Testament (27) ---
        new("Matthew", BibleTestament.New, ["matthew", "matt", "от матфея", "евангелие от матфея", "матфея", "мф"]),
        new("Mark", BibleTestament.New, ["mark", "от марка", "евангелие от марка", "марка", "мк"]),
        new("Luke", BibleTestament.New, ["luke", "lk", "от луки", "евангелие от луки", "луки", "лука", "лк"]),
        new("John", BibleTestament.New, ["john", "jn", "от иоанна", "евангелие от иоанна", "иоанна", "иоанн", "ин"]),
        new("Acts", BibleTestament.New, ["acts", "деяния", "деяний", "деян"]),
        new("Romans", BibleTestament.New, ["romans", "rom", "к римлянам", "римлянам", "рим"]),
        new("1 Corinthians", BibleTestament.New,
            ["1 corinthians", "1st corinthians", "i corinthians", "first corinthians", "1-е коринфянам", "1 коринфянам", "первое коринфянам", "1 кор", "1-е кор"]),
        new("2 Corinthians", BibleTestament.New,
            ["2 corinthians", "2nd corinthians", "ii corinthians", "second corinthians", "2-е коринфянам", "2 коринфянам", "второе коринфянам", "2 кор", "2-е кор"]),
        new("Galatians", BibleTestament.New, ["galatians", "gal", "к галатам", "галатам", "гал"]),
        new("Ephesians", BibleTestament.New, ["ephesians", "eph", "к ефесянам", "ефесянам", "еф"]),
        new("Philippians", BibleTestament.New, ["philippians", "phil", "к филиппийцам", "филиппийцам", "флп"]),
        new("Colossians", BibleTestament.New, ["colossians", "col", "к колоссянам", "колоссянам", "кол"]),
        new("1 Thessalonians", BibleTestament.New,
            ["1 thessalonians", "1st thessalonians", "i thessalonians", "first thessalonians", "1-е фессалоникийцам", "1 фессалоникийцам", "первое фессалоникийцам"]),
        new("2 Thessalonians", BibleTestament.New,
            ["2 thessalonians", "2nd thessalonians", "ii thessalonians", "second thessalonians", "2-е фессалоникийцам", "2 фессалоникийцам", "второе фессалоникийцам"]),
        new("1 Timothy", BibleTestament.New,
            ["1 timothy", "1st timothy", "i timothy", "first timothy", "1-е тимофею", "1 тимофею", "первое тимофею"]),
        new("2 Timothy", BibleTestament.New,
            ["2 timothy", "2 tim", "2nd timothy", "ii timothy", "second timothy", "2-е тимофею", "2 тимофею", "второе тимофею", "2 тим"]),
        new("Titus", BibleTestament.New, ["titus", "к титу", "титу", "тит"]),
        new("Philemon", BibleTestament.New, ["philemon", "к филимону", "филимону"]),
        new("Hebrews", BibleTestament.New, ["hebrews", "heb", "к евреям", "евреям", "евр"]),
        new("James", BibleTestament.New, ["james", "иакова"]),
        new("1 Peter", BibleTestament.New,
            ["1 peter", "1st peter", "i peter", "first peter", "1-е петра", "1 петра", "первое петра"]),
        new("2 Peter", BibleTestament.New,
            ["2 peter", "2nd peter", "ii peter", "second peter", "2-е петра", "2 петра", "второе петра"]),
        new("1 John", BibleTestament.New,
            ["1 john", "1st john", "i john", "first john", "1-е иоанна", "1 иоанна", "первое иоанна"]),
        new("2 John", BibleTestament.New,
            ["2 john", "2nd john", "ii john", "second john", "2-е иоанна", "2 иоанна", "второе иоанна"]),
        new("3 John", BibleTestament.New,
            ["3 john", "3rd john", "iii john", "third john", "3-е иоанна", "3 иоанна", "третье иоанна"]),
        new("Jude", BibleTestament.New, ["jude", "иуды"]),
        new("Revelation", BibleTestament.New, ["revelation", "revelations", "откровение", "откр"])
    ];

    /// <summary>
    /// Matches a free-text reference to the book whose prefix list has the longest match - longest
    /// wins specifically so a numbered epistle (e.g. "1 John") is preferred over the unnumbered book
    /// it shares a root with (e.g. "John") when the text actually starts with the numeral.
    /// </summary>
    public static BibleBook? TryMatch(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        // Strip periods so abbreviations like "2 Кор." or "Тит." normalize the same as their
        // unpunctuated form ("2 кор", "тит") already in MatchPrefixes.
        var normalized = reference.Trim().ToLowerInvariant().Replace('ё', 'е').Replace(".", "");

        BibleBook? best = null;
        var bestLength = 0;

        foreach (var book in AllBooks)
        {
            foreach (var prefix in book.MatchPrefixes)
            {
                if (prefix.Length <= bestLength || !normalized.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // Require a non-letter boundary right after the prefix (space, digit, colon, hyphen,
                // end of string) so e.g. "john" doesn't match inside some longer unrelated word.
                var nextChar = normalized.Length > prefix.Length ? normalized[prefix.Length] : '\0';
                if (nextChar != '\0' && char.IsLetter(nextChar))
                {
                    continue;
                }

                best = book;
                bestLength = prefix.Length;
            }
        }

        return best;
    }
}
