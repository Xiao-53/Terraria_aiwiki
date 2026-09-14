using System.Text;
using SQLite;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services;

public partial class DatabaseService
{
    public async Task SavePageWithSearchAsync(
        WikiPage page,
        ProcessedWikiContent processed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(processed);
        if (Mode != DbMode.Content)
            throw new InvalidOperationException("Search regions can only be saved in a content database.");

        await Init(cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _db.RunInTransactionAsync(transaction =>
        {
            var existing = transaction.Query<WikiSearchRegion>(
                "SELECT * FROM WikiSearchRegion WHERE PageTitle = ? ORDER BY RegionOrdinal", page.Title);
            foreach (var region in existing)
            {
                transaction.Execute(
                    "INSERT INTO WikiSearchRegionFts(WikiSearchRegionFts, rowid, ContextTokens, BodyTokens) VALUES('delete', ?, ?, ?)",
                    region.Id,
                    WikiSearchText.EncodeForIndex(region.ContextText),
                    WikiSearchText.EncodeForIndex(region.BodyText));
            }
            transaction.Execute("DELETE FROM WikiSearchRegion WHERE PageTitle = ?", page.Title);
            transaction.Execute("DELETE FROM WikiSearchSection WHERE PageTitle = ?", page.Title);
            transaction.InsertOrReplace(page);

            foreach (var draft in processed.SearchSections)
            {
                transaction.Insert(new WikiSearchSection
                {
                    PageTitle = page.Title,
                    SectionOrdinal = draft.SectionOrdinal,
                    Title = draft.Title,
                    HeadingPath = draft.HeadingPath,
                    Anchor = draft.Anchor,
                });
            }

            foreach (var draft in processed.SearchRegions)
            {
                var region = new WikiSearchRegion
                {
                    PageTitle = page.Title,
                    SectionOrdinal = draft.SectionOrdinal,
                    RegionOrdinal = draft.RegionOrdinal,
                    Kind = draft.Kind,
                    HeadingPath = draft.HeadingPath,
                    Anchor = draft.Anchor,
                    ContextText = draft.ContextText,
                    BodyText = draft.BodyText,
                };
                transaction.Insert(region);
                transaction.Execute(
                    "INSERT INTO WikiSearchRegionFts(rowid, ContextTokens, BodyTokens) VALUES(?, ?, ?)",
                    region.Id,
                    WikiSearchText.EncodeForIndex(region.ContextText),
                    WikiSearchText.EncodeForIndex(region.BodyText));
            }
        }).WaitAsync(cancellationToken);
    }

    public async Task DeletePageWithSearchAsync(
        string pageTitle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageTitle)) return;
        await Init(cancellationToken: cancellationToken);
        await _db.RunInTransactionAsync(transaction =>
        {
            DeleteRegionIndex(transaction, "WHERE PageTitle = ?", pageTitle);
            transaction.Execute("DELETE FROM WikiSearchSection WHERE PageTitle = ?", pageTitle);
            transaction.Execute("DELETE FROM WikiPage WHERE Title = ?", pageTitle);
        }).WaitAsync(cancellationToken);
    }

    private async Task DeleteAllPagesWithSearchAsync(CancellationToken cancellationToken)
    {
        await Init(cancellationToken: cancellationToken);
        await _db.RunInTransactionAsync(transaction =>
        {
            DeleteRegionIndex(transaction, string.Empty);
            transaction.Execute("DELETE FROM WikiSearchSection");
            transaction.Execute("DELETE FROM WikiPage");
        }).WaitAsync(cancellationToken);
    }

    private static void DeleteRegionIndex(SQLiteConnection transaction, string whereClause, params object[] arguments)
    {
        var existing = transaction.Query<WikiSearchRegion>(
            $"SELECT * FROM WikiSearchRegion {whereClause} ORDER BY RegionOrdinal", arguments);
        foreach (var region in existing)
        {
            transaction.Execute(
                "INSERT INTO WikiSearchRegionFts(WikiSearchRegionFts, rowid, ContextTokens, BodyTokens) VALUES('delete', ?, ?, ?)",
                region.Id,
                WikiSearchText.EncodeForIndex(region.ContextText),
                WikiSearchText.EncodeForIndex(region.BodyText));
        }
        transaction.Execute($"DELETE FROM WikiSearchRegion {whereClause}", arguments);
    }

    public async Task<List<SearchResultItem>> SearchAsync(
        string keyword,
        int maxSearchItemCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxSearchItemCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxSearchItemCount),
                "The maximum result count must be between 1 and 100.");

        var concepts = WikiSearchText.SplitConcepts(keyword);
        if (concepts.Count == 0) return [];
        await Init(cancellationToken: cancellationToken);

        var resolvedCandidates = new List<ResolvedConcept>(concepts.Count);
        for (var index = 0; index < concepts.Count; index++)
            resolvedCandidates.Add(await ResolveConceptAsync(index, concepts[index], cancellationToken));
        var resolved = CollapseResolvedConcepts(resolvedCandidates);
        var conceptCount = resolved.Count;

        var regions = new Dictionary<long, RegionHit>();
        var pages = new Dictionary<string, PageHit>(StringComparer.Ordinal);
        var fetchLimit = Math.Clamp(maxSearchItemCount * 40, 400, 4000);

        foreach (var concept in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (concept.IsEntity)
            {
                var page = GetOrCreatePage(pages, concept.Canonical);
                page.IdentityConcepts.Add(concept.Ordinal);
                if (concept.IdentityQuality > page.IdentityQuality)
                {
                    page.IdentityQuality = concept.IdentityQuality;
                    page.IdentityKind = concept.IdentityKind;
                    page.DisplayTitle = concept.IdentityKind == WikiSearchEvidenceKind.ExactRedirect
                        ? concept.Original
                        : concept.Canonical;
                    page.RedirectTo = concept.IdentityKind == WikiSearchEvidenceKind.ExactRedirect
                        ? concept.Canonical
                        : string.Empty;
                }
            }

            foreach (var variant in concept.Variants)
            {
                var phrase = WikiSearchText.EncodePhrase(variant);
                if (phrase.Length == 0) continue;
                var hits = await _db.QueryAsync<RegionQueryRow>(
                    """
                    SELECT region.*, bm25(WikiSearchRegionFts, 2.0, 1.0) AS RankScore
                    FROM WikiSearchRegionFts
                    INNER JOIN WikiSearchRegion AS region ON region.Id = WikiSearchRegionFts.rowid
                    INNER JOIN WikiPage AS page ON page.Title = region.PageTitle
                    WHERE WikiSearchRegionFts MATCH ?
                    ORDER BY RankScore, region.PageTitle, region.RegionOrdinal
                    LIMIT ?
                    """,
                    phrase,
                    fetchLimit).WaitAsync(cancellationToken);
                foreach (var hit in hits)
                {
                    if (!regions.TryGetValue(hit.Id, out var region))
                    {
                        region = new RegionHit(hit);
                        regions.Add(hit.Id, region);
                    }
                    region.Concepts.Add(concept.Ordinal);
                    region.RankScore = Math.Min(region.RankScore, hit.RankScore);
                    GetOrCreatePage(pages, hit.PageTitle).RegionIds.Add(hit.Id);
                }
            }

            await AddTitleCandidatesAsync(concept, pages, regions, fetchLimit, cancellationToken);
        }

        await AddEntityPageCandidatesAsync(resolved, pages, regions, fetchLimit, cancellationToken);

        foreach (var page in pages.Values.Where(page => page.RegionIds.Count == 0
                     || page.IdentityConcepts.Count > 0).ToArray())
        {
            var lead = await _db.QueryAsync<RegionQueryRow>(
                "SELECT *, 0.0 AS RankScore FROM WikiSearchRegion WHERE PageTitle = ? ORDER BY CASE WHEN Kind = 'paragraph' THEN 0 ELSE 1 END, RegionOrdinal LIMIT 1",
                page.CanonicalTitle).WaitAsync(cancellationToken);
            if (lead.Count == 0) continue;
            var region = regions.TryGetValue(lead[0].Id, out var existingRegion)
                ? existingRegion : new RegionHit(lead[0]);
            foreach (var concept in page.PageConcepts) region.Concepts.Add(concept);
            foreach (var concept in page.IdentityConcepts) region.Concepts.Add(concept);
            regions[region.Row.Id] = region;
            page.RegionIds.Add(region.Row.Id);
        }

        var results = new List<RankedResult>();
        foreach (var page in pages.Values)
        {
            var pageRegions = page.RegionIds.Where(regions.ContainsKey).Select(id => regions[id]).ToArray();
            if (pageRegions.Length == 0) continue;
            var pageCoverage = pageRegions.SelectMany(region => region.Concepts)
                .Concat(page.IdentityConcepts).Concat(page.PageConcepts).ToHashSet();
            var sectionCoverage = pageRegions.GroupBy(region => region.Row.SectionOrdinal)
                .ToDictionary(group => group.Key, group => group.SelectMany(region => region.Concepts).ToHashSet());

            foreach (var region in pageRegions)
            {
                var scope = region.Concepts.Count == conceptCount
                    ? WikiSearchScope.SameRegion
                    : sectionCoverage[region.Row.SectionOrdinal].Count == conceptCount
                        ? WikiSearchScope.SameSection
                        : pageCoverage.Count == conceptCount
                            ? WikiSearchScope.SamePage
                            : WikiSearchScope.Partial;
                var evidence = BuildEvidence(region.Row);
                var preview = BuildPreview(evidence, resolved);
                var highlights = BuildHighlights(preview, resolved);
                var kind = page.IdentityKind is WikiSearchEvidenceKind.ExactRedirect or WikiSearchEvidenceKind.ExactCommunityAlias
                    ? page.IdentityKind
                    : page.IdentityKind == WikiSearchEvidenceKind.ExactTitle && conceptCount == 1
                        ? page.IdentityKind
                        : MapEvidenceKind(region.Row.Kind);
                var matchKind = page.IdentityKind switch
                {
                    WikiSearchEvidenceKind.ExactTitle => WikiSearchMatchKind.ExactTitle,
                    WikiSearchEvidenceKind.ExactRedirect => WikiSearchMatchKind.ExactRedirect,
                    WikiSearchEvidenceKind.ExactCommunityAlias => WikiSearchMatchKind.ExactTitle,
                    _ => page.PageMatchKind ?? WikiSearchMatchKind.BodyFts,
                };
                var contextMatches = CountMatches(region.Row.ContextText, resolved);
                var bodyMatches = CountMatches(region.Row.BodyText, resolved);
                var supportingRegions = scope switch
                {
                    WikiSearchScope.SameSection => pageRegions
                        .Where(candidate => candidate.Row.SectionOrdinal == region.Row.SectionOrdinal),
                    WikiSearchScope.SamePage => pageRegions.AsEnumerable(),
                    _ => [region],
                };
                results.Add(new RankedResult(
                    new SearchResultItem
                    {
                        Title = page.DisplayTitle.Length == 0 ? page.CanonicalTitle : page.DisplayTitle,
                        RedirectTo = page.RedirectTo,
                        CanonicalTitle = page.CanonicalTitle,
                        RegionId = region.Row.Id,
                        RegionOrdinal = region.Row.RegionOrdinal,
                        SectionPath = region.Row.HeadingPath,
                        Anchor = region.Row.Anchor,
                        EvidenceText = evidence,
                        EvidenceRegions = supportingRegions
                            .OrderBy(candidate => candidate.Row.RegionOrdinal)
                            .Select(ToEvidenceBlock)
                            .ToArray(),
                        Snippet = preview,
                        Highlights = highlights,
                        MatchKind = matchKind,
                        EvidenceKind = kind,
                        Scope = scope,
                    },
                    pageCoverage.Count,
                    page.IdentityConcepts.Count,
                    page.IdentityQuality,
                    contextMatches,
                    bodyMatches,
                    conceptCount == 1 && region.Row.Kind == "paragraph" ? -1 : EvidencePriority(region.Row.Kind),
                    region.RankScore));
            }
        }

        var ranked = results
            .GroupBy(result => (result.Item.CanonicalTitle, result.Item.EvidenceText), StringTupleComparer.Instance)
            .Select(group => group.OrderBy(result => result.Item.RegionOrdinal).First())
            .OrderBy(result => result.Item.Scope)
            // For a single name, related titles should precede incidental body mentions.
            .ThenBy(result => conceptCount == 1 ? (int)result.Item.MatchKind : 0)
            .ThenByDescending(result => result.IdentityConceptCount)
            .ThenByDescending(result => result.CoverageCount)
            .ThenBy(result => result.EvidencePriority)
            .ThenBy(result => conceptCount == 1 && result.Item.MatchKind != WikiSearchMatchKind.BodyFts
                && result.EvidencePriority == -1 ? result.Item.RegionOrdinal : 0)
            .ThenByDescending(result => result.ContextMatches * 2 + result.BodyMatches)
            .ThenByDescending(result => result.IdentityQuality)
            .ThenBy(result => result.RankScore)
            .ThenBy(result => result.Item.CanonicalTitle, StringComparer.Ordinal)
            .ThenBy(result => result.Item.RegionOrdinal);

        return ranked.GroupBy(result => result.Item.CanonicalTitle, StringComparer.Ordinal)
            .Take(maxSearchItemCount)
            .Select(group =>
            {
                var best = group.First().Item;
                best.EvidenceRegions = group.SelectMany(result => result.Item.EvidenceRegions)
                    .DistinctBy(region => region.RegionId)
                    .OrderBy(region => region.RegionOrdinal)
                    .ToArray();
                return best;
            }).ToList();
    }

    private static IReadOnlyList<ResolvedConcept> CollapseResolvedConcepts(
        IEnumerable<ResolvedConcept> concepts) => concepts
        .GroupBy(
            concept => (concept.IsEntity ? "entity:" : "term:") + concept.Canonical,
            StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select((group, ordinal) =>
        {
            var best = group
                .OrderByDescending(concept => concept.IdentityQuality)
                .ThenBy(concept => concept.Canonical, StringComparer.Ordinal)
                .ThenBy(concept => concept.Original, StringComparer.Ordinal)
                .First();
            return best with
            {
                Ordinal = ordinal,
                Variants = group.SelectMany(concept => concept.Variants)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
        })
        .ToArray();

    private async Task<ResolvedConcept> ResolveConceptAsync(
        int ordinal,
        string value,
        CancellationToken cancellationToken)
    {
        var title = await _db.ExecuteScalarAsync<string?>(
            "SELECT Title FROM WikiPage WHERE Title = ? COLLATE NOCASE LIMIT 1", value)
            .WaitAsync(cancellationToken);
        if (!string.IsNullOrEmpty(title))
            return new ResolvedConcept(ordinal, value, title, true, 3,
                WikiSearchEvidenceKind.ExactTitle, [value, title]);

        var redirect = await _db.QueryAsync<RedirectQueryRow>(
            "SELECT FromName, ToTarget FROM WikiRedirect WHERE FromName = ? COLLATE NOCASE LIMIT 1", value)
            .WaitAsync(cancellationToken);
        if (redirect.Count > 0)
        {
            var target = SplitAnchor(redirect[0].ToTarget).Title;
            var targetExists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM WikiPage WHERE Title = ?", target).WaitAsync(cancellationToken);
            if (targetExists > 0)
            {
                var variants = target.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || value.Contains(target, StringComparison.OrdinalIgnoreCase)
                    ? new[] { value, target }
                    : [target];
                return new ResolvedConcept(ordinal, value, target, true, 2,
                    WikiSearchEvidenceKind.ExactRedirect, variants);
            }
        }

        if (_searchAliases.TryResolve(_wikiId, value, out var aliasTarget))
        {
            var target = await _db.ExecuteScalarAsync<string?>(
                "SELECT Title FROM WikiPage WHERE Title = ? COLLATE NOCASE LIMIT 1", aliasTarget)
                .WaitAsync(cancellationToken);
            if (!string.IsNullOrEmpty(target))
                return new ResolvedConcept(ordinal, value, target, true, 1,
                    WikiSearchEvidenceKind.ExactCommunityAlias, [target]);
        }

        return new ResolvedConcept(ordinal, value, value, false, 0,
            WikiSearchEvidenceKind.Body, [value]);
    }

    private async Task AddTitleCandidatesAsync(
        ResolvedConcept concept,
        IDictionary<string, PageHit> pages,
        IDictionary<long, RegionHit> regions,
        int limit,
        CancellationToken cancellationToken)
    {
        var escaped = EscapeLikePattern(concept.Original);
        var rows = await _db.QueryAsync<TitleCandidateRow>(
            """
            SELECT Title, CASE WHEN Title LIKE ? ESCAPE '\' THEN 2 ELSE 4 END AS MatchKind
            FROM WikiPage
            WHERE Title LIKE ? ESCAPE '\'
            ORDER BY LENGTH(Title), Title
            LIMIT ?
            """,
            escaped + "%",
            "%" + escaped + "%",
            limit).WaitAsync(cancellationToken);
        foreach (var row in rows)
        {
            var page = GetOrCreatePage(pages, row.Title);
            page.PageConcepts.Add(concept.Ordinal);
            page.PageMatchKind ??= (WikiSearchMatchKind)row.MatchKind;
            var lead = await _db.QueryAsync<RegionQueryRow>(
                "SELECT *, 0.0 AS RankScore FROM WikiSearchRegion WHERE PageTitle = ? ORDER BY CASE WHEN Kind = 'paragraph' THEN 0 ELSE 1 END, RegionOrdinal LIMIT 1",
                row.Title).WaitAsync(cancellationToken);
            if (lead.Count == 0) continue;
            if (!regions.TryGetValue(lead[0].Id, out var region))
            {
                region = new RegionHit(lead[0]);
                regions[lead[0].Id] = region;
            }
            region.Concepts.Add(concept.Ordinal);
            page.RegionIds.Add(lead[0].Id);
        }
    }

    private async Task AddEntityPageCandidatesAsync(
        IReadOnlyList<ResolvedConcept> concepts,
        IDictionary<string, PageHit> pages,
        IDictionary<long, RegionHit> regions,
        int limit,
        CancellationToken cancellationToken)
    {
        foreach (var entity in concepts.Where(concept => concept.IsEntity))
        {
            var page = GetOrCreatePage(pages, entity.Canonical);
            foreach (var concept in concepts.Where(concept => concept.Ordinal != entity.Ordinal))
            {
                foreach (var variant in concept.Variants)
                {
                    var phrase = WikiSearchText.EncodePhrase(variant);
                    if (phrase.Length == 0) continue;
                    var hits = await _db.QueryAsync<RegionQueryRow>(
                        """
                        SELECT region.*, bm25(WikiSearchRegionFts, 2.0, 1.0) AS RankScore
                        FROM WikiSearchRegionFts
                        INNER JOIN WikiSearchRegion AS region ON region.Id = WikiSearchRegionFts.rowid
                        WHERE WikiSearchRegionFts MATCH ? AND region.PageTitle = ?
                        ORDER BY RankScore, region.RegionOrdinal
                        LIMIT ?
                        """,
                        phrase,
                        entity.Canonical,
                        limit).WaitAsync(cancellationToken);
                    foreach (var hit in hits)
                    {
                        if (!regions.TryGetValue(hit.Id, out var region))
                        {
                            region = new RegionHit(hit);
                            regions.Add(hit.Id, region);
                        }
                        region.Concepts.Add(concept.Ordinal);
                        region.Concepts.Add(entity.Ordinal);
                        region.RankScore = Math.Min(region.RankScore, hit.RankScore);
                        page.RegionIds.Add(hit.Id);
                    }
                }
            }

            foreach (var regionId in page.RegionIds)
                if (regions.TryGetValue(regionId, out var region))
                    region.Concepts.Add(entity.Ordinal);
        }
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static PageHit GetOrCreatePage(IDictionary<string, PageHit> pages, string title)
    {
        if (!pages.TryGetValue(title, out var page))
        {
            page = new PageHit(title);
            pages.Add(title, page);
        }
        return page;
    }

    private static string BuildEvidence(RegionQueryRow region) =>
        string.IsNullOrWhiteSpace(region.ContextText)
            ? region.BodyText
            : string.IsNullOrWhiteSpace(region.BodyText)
                ? region.ContextText
                : region.ContextText + Environment.NewLine + region.BodyText;

    private static string BuildPreview(string evidence, IReadOnlyList<ResolvedConcept> concepts)
    {
        const int maxLength = 180;
        if (evidence.Length <= maxLength) return evidence;
        var first = concepts.SelectMany(concept => concept.Variants)
            .Select(variant => evidence.IndexOf(variant, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();
        var start = Math.Max(0, first - 50);
        if (start > 0 && char.IsLowSurrogate(evidence[start])) start--;
        var length = Math.Min(maxLength, evidence.Length - start);
        if (start + length < evidence.Length && char.IsHighSurrogate(evidence[start + length - 1])) length--;
        return (start > 0 ? "…" : string.Empty)
            + evidence.Substring(start, length)
            + (start + length < evidence.Length ? "…" : string.Empty);
    }

    private static IReadOnlyList<WikiSearchHighlight> BuildHighlights(
        string preview,
        IReadOnlyList<ResolvedConcept> concepts)
    {
        var ranges = new List<WikiSearchHighlight>();
        foreach (var term in concepts.SelectMany(concept => concept.Variants)
                     .Where(term => term.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var start = 0;
            while ((start = preview.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                ranges.Add(new WikiSearchHighlight(start, term.Length));
                start += term.Length;
            }
        }
        return ranges.OrderBy(range => range.Start)
            .Aggregate(new List<WikiSearchHighlight>(), (result, range) =>
            {
                if (result.Count == 0 || result[^1].Start + result[^1].Length < range.Start)
                    result.Add(range);
                else
                {
                    var previous = result[^1];
                    var end = Math.Max(previous.Start + previous.Length, range.Start + range.Length);
                    result[^1] = new WikiSearchHighlight(previous.Start, end - previous.Start);
                }
                return result;
            });
    }

    private static int CountMatches(string text, IEnumerable<ResolvedConcept> concepts) => concepts.Count(concept =>
        concept.Variants.Any(variant => text.Contains(variant, StringComparison.OrdinalIgnoreCase)));

    private static WikiSearchEvidenceKind MapEvidenceKind(string kind) => kind switch
    {
        "heading" => WikiSearchEvidenceKind.Heading,
        "table_row" => WikiSearchEvidenceKind.TableRow,
        "list_item" => WikiSearchEvidenceKind.ListItem,
        "definition" => WikiSearchEvidenceKind.Definition,
        "infobox" => WikiSearchEvidenceKind.Infobox,
        "generic" => WikiSearchEvidenceKind.Generic,
        _ => WikiSearchEvidenceKind.Body,
    };

    private static WikiSearchEvidenceBlock ToEvidenceBlock(RegionHit region) => new(
        region.Row.Id,
        region.Row.RegionOrdinal,
        region.Row.HeadingPath,
        region.Row.Anchor,
        MapEvidenceKind(region.Row.Kind),
        BuildEvidence(region.Row));

    private static int EvidencePriority(string kind) => kind switch
    {
        "infobox" => 0,
        "table_row" => 1,
        "list_item" or "definition" => 2,
        "paragraph" => 3,
        "heading" => 4,
        _ => 5,
    };

    private async Task ValidateOrPrepareContentFormatAsync(
        WikiBook? seedBook,
        CancellationToken cancellationToken)
    {
        var tableCount = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
            .WaitAsync(cancellationToken);
        if (tableCount == 0)
        {
            _wikiId = seedBook?.Id ?? 0;
            return;
        }

        var metadataExists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'WikiDatabaseMetadata'")
            .WaitAsync(cancellationToken);
        if (metadataExists == 0)
            throw new WikiDatabaseFormatException("content-database-format-unsupported",
                "The content database uses an unsupported development format and must be downloaded again.");

        var version = await _db.ExecuteScalarAsync<string?>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'format_version'")
            .WaitAsync(cancellationToken);
        if (!int.TryParse(version, out var parsed) || parsed != ContentFormatVersion)
            throw new WikiDatabaseFormatException("content-database-format-unsupported",
                "The content database format is not supported by this application version.");

        var extractorVersion = await _db.ExecuteScalarAsync<string?>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'extractor_version'")
            .WaitAsync(cancellationToken);
        if (!int.TryParse(extractorVersion, out var parsedExtractor) || parsedExtractor != SearchExtractorVersion)
            throw new WikiDatabaseFormatException("content-database-processing-unsupported",
                "The content database search processing version is not supported by this application version.");

        var wikiId = await _db.ExecuteScalarAsync<string?>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'wiki_id'")
            .WaitAsync(cancellationToken);
        _wikiId = int.TryParse(wikiId, out var id) ? id : seedBook?.Id ?? 0;
        if (seedBook is not null && _wikiId != 0 && _wikiId != seedBook.Id)
            throw new WikiDatabaseFormatException("content-database-wiki-mismatch",
                "The content database belongs to another Wiki source.");
    }

    private async Task InitContentMetadataAsync(WikiBook? seedBook, CancellationToken cancellationToken)
    {
        _wikiId = _wikiId == 0 ? seedBook?.Id ?? 0 : _wikiId;
        await _db.InsertOrReplaceAsync(new WikiDatabaseMetadata
        {
            Key = "format_version", Value = ContentFormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).WaitAsync(cancellationToken);
        await _db.InsertOrReplaceAsync(new WikiDatabaseMetadata
        {
            Key = "extractor_version", Value = SearchExtractorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).WaitAsync(cancellationToken);
        await _db.InsertOrReplaceAsync(new WikiDatabaseMetadata
        {
            Key = "wiki_id", Value = _wikiId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }).WaitAsync(cancellationToken);
    }

    private static (string Title, string Anchor) SplitAnchor(string value)
    {
        var index = value.IndexOf('#');
        return index < 0 ? (value, string.Empty) : (value[..index], value[(index + 1)..]);
    }

    private sealed record ResolvedConcept(
        int Ordinal,
        string Original,
        string Canonical,
        bool IsEntity,
        int IdentityQuality,
        WikiSearchEvidenceKind IdentityKind,
        IReadOnlyList<string> Variants);

    private sealed class PageHit(string title)
    {
        public string CanonicalTitle { get; } = title;
        public string DisplayTitle { get; set; } = title;
        public string RedirectTo { get; set; } = string.Empty;
        public int IdentityQuality { get; set; }
        public WikiSearchEvidenceKind IdentityKind { get; set; } = WikiSearchEvidenceKind.Body;
        public WikiSearchMatchKind? PageMatchKind { get; set; }
        public HashSet<int> IdentityConcepts { get; } = [];
        public HashSet<int> PageConcepts { get; } = [];
        public HashSet<long> RegionIds { get; } = [];
    }

    private sealed class RegionHit(RegionQueryRow row)
    {
        public RegionQueryRow Row { get; } = row;
        public HashSet<int> Concepts { get; } = [];
        public double RankScore { get; set; } = row.RankScore;
    }

    private sealed class RegionQueryRow
    {
        public long Id { get; set; }
        public string PageTitle { get; set; } = string.Empty;
        public int SectionOrdinal { get; set; }
        public int RegionOrdinal { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string HeadingPath { get; set; } = string.Empty;
        public string Anchor { get; set; } = string.Empty;
        public string ContextText { get; set; } = string.Empty;
        public string BodyText { get; set; } = string.Empty;
        public double RankScore { get; set; }
    }

    private sealed class RedirectQueryRow
    {
        public string FromName { get; set; } = string.Empty;
        public string ToTarget { get; set; } = string.Empty;
    }

    private sealed class TitleCandidateRow
    {
        public string Title { get; set; } = string.Empty;
        public int MatchKind { get; set; }
    }

    private sealed record RankedResult(
        SearchResultItem Item,
        int CoverageCount,
        int IdentityConceptCount,
        int IdentityQuality,
        int ContextMatches,
        int BodyMatches,
        int EvidencePriority,
        double RankScore);

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static StringTupleComparer Instance { get; } = new();
        public bool Equals((string, string) x, (string, string) y) =>
            StringComparer.Ordinal.Equals(x.Item1, y.Item1) && StringComparer.Ordinal.Equals(x.Item2, y.Item2);
        public int GetHashCode((string, string) obj) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.Item1), StringComparer.Ordinal.GetHashCode(obj.Item2));
    }
}

public sealed class WikiDatabaseFormatException : Exception
{
    public WikiDatabaseFormatException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
