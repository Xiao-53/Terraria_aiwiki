from __future__ import annotations

import argparse
import json
import os
import sqlite3
import statistics
import time
import unicodedata
from pathlib import Path


QUERIES = [
    ("光女 宝藏袋", ["光之女皇", "宝藏袋"], "光之女皇"),
    ("猪鲨 武器", ["猪龙鱼公爵", "武器"], "猪龙鱼公爵"),
    ("克眼 掉落", ["克苏鲁之眼", "掉落"], "克苏鲁之眼"),
    ("剑", ["剑"], "剑"),
    ("弓", ["弓"], "弓"),
    ("武器", ["武器"], "武器"),
    ("药水", ["药水"], "药水"),
    ("饰品", ["配饰"], "配饰"),
]


def is_han(character: str) -> bool:
    value = ord(character)
    return (
        0x3400 <= value <= 0x4DBF
        or 0x4E00 <= value <= 0x9FFF
        or 0xF900 <= value <= 0xFAFF
        or 0x20000 <= value <= 0x2FA1F
    )


def encode_unigram_bigram(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value or "").lower()
    tokens: list[str] = []
    word: list[str] = []
    previous_han: str | None = None

    def flush_word() -> None:
        if word:
            tokens.append("w" + "".join(word).encode("utf-8").hex())
            word.clear()

    for character in normalized:
        if is_han(character):
            flush_word()
            current = f"{ord(character):x}"
            tokens.append("h" + current)
            if previous_han is not None:
                tokens.append("b" + previous_han + "x" + current)
            previous_han = current
        elif character.isalnum():
            previous_han = None
            word.append(character)
        else:
            flush_word()
            previous_han = None
    flush_word()
    return " ".join(tokens)


def encode_current(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value or "").lower()
    tokens: list[str] = []
    word: list[str] = []

    def flush_word() -> None:
        if word:
            tokens.append("w" + "".join(word).encode("utf-8").hex())
            word.clear()

    for character in normalized:
        if is_han(character):
            flush_word()
            tokens.append(f"h{ord(character):x}")
        elif character.isalnum():
            word.append(character)
        else:
            flush_word()
            if not character.isspace() and not unicodedata.category(character).startswith("C"):
                tokens.append(f"p{ord(character):x}")
    flush_word()
    return " ".join(tokens)


def current_query(concepts: list[str]) -> str:
    return " AND ".join(
        f'"{encode_current(concept)}"'
        for concept in concepts
        if encode_current(concept)
    )


def bigram_query(concepts: list[str]) -> str:
    terms: list[str] = []
    for concept in concepts:
        han = [character for character in unicodedata.normalize("NFKC", concept) if is_han(character)]
        if len(han) == 1:
            terms.append(f'h{ord(han[0]):x}')
        elif len(han) > 1:
            terms.extend(
                f'b{ord(left):x}x{ord(right):x}'
                for left, right in zip(han, han[1:])
            )
    return " AND ".join(f'"{term}"' for term in terms)


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    index = max(0, min(len(ordered) - 1, int(len(ordered) * fraction + 0.999999) - 1))
    return ordered[index]


def summary(values: list[float]) -> dict[str, float | int]:
    return {
        "samples": len(values),
        "p50_ms": percentile(values, 0.50),
        "p95_ms": percentile(values, 0.95),
        "min_ms": min(values),
        "max_ms": max(values),
        "mean_ms": statistics.fmean(values),
    }


def reset_database(path: Path) -> sqlite3.Connection:
    if path.exists():
        path.unlink()
    connection = sqlite3.connect(path)
    connection.execute("pragma journal_mode=wal")
    connection.execute("pragma synchronous=normal")
    connection.execute("pragma temp_store=memory")
    return connection


def build_trigram(source: Path, target: Path) -> tuple[float, int]:
    connection = reset_database(target)
    connection.execute("attach database ? as source", (str(source),))
    started = time.perf_counter()
    connection.execute(
        "create virtual table RegionFts using fts5(ContextText, BodyText, content='', tokenize='trigram', detail=full)"
    )
    connection.execute(
        "insert into RegionFts(rowid, ContextText, BodyText) "
        "select Id, ContextText, BodyText from source.WikiSearchRegion"
    )
    connection.commit()
    connection.execute("insert into RegionFts(RegionFts) values('optimize')")
    connection.commit()
    elapsed = time.perf_counter() - started
    connection.execute("pragma journal_mode=delete")
    connection.execute("vacuum")
    connection.close()
    return elapsed, target.stat().st_size


def build_encoded(source: Path, target: Path, encoder) -> tuple[float, int]:
    source_connection = sqlite3.connect(f"file:{source}?mode=ro", uri=True)
    target_connection = reset_database(target)
    target_connection.execute(
        "create virtual table RegionFts using fts5(ContextTokens, BodyTokens, content='', tokenize='unicode61', detail=full)"
    )
    started = time.perf_counter()
    cursor = source_connection.execute(
        "select Id, ContextText, BodyText from WikiSearchRegion order by Id"
    )
    batch: list[tuple[int, str, str]] = []
    for row_id, context, body in cursor:
        batch.append((row_id, encoder(context), encoder(body)))
        if len(batch) >= 1000:
            target_connection.executemany(
                "insert into RegionFts(rowid, ContextTokens, BodyTokens) values(?, ?, ?)", batch
            )
            batch.clear()
    if batch:
        target_connection.executemany(
            "insert into RegionFts(rowid, ContextTokens, BodyTokens) values(?, ?, ?)", batch
        )
    target_connection.commit()
    target_connection.execute("insert into RegionFts(RegionFts) values('optimize')")
    target_connection.commit()
    elapsed = time.perf_counter() - started
    target_connection.execute("pragma journal_mode=delete")
    target_connection.execute("vacuum")
    target_connection.close()
    source_connection.close()
    return elapsed, target.stat().st_size


def benchmark_trigram(index_path: Path, source: Path) -> list[dict[str, object]]:
    connection = sqlite3.connect(index_path)
    connection.execute("attach database ? as source", (str(source),))
    results = []
    for display, concepts, expected in QUERIES:
        long_terms = [term for term in concepts if len(term) >= 3]
        short_terms = [term for term in concepts if len(term) < 3]
        parameters: list[object] = []
        conditions: list[str] = []
        from_clause = "source.WikiSearchRegion AS region"
        rank = "0.0"
        if long_terms:
            from_clause = "RegionFts JOIN source.WikiSearchRegion AS region ON region.Id=RegionFts.rowid"
            conditions.append("RegionFts MATCH ?")
            parameters.append(" AND ".join(f'\"{term.replace(chr(34), chr(34) * 2)}\"' for term in long_terms))
            rank = "bm25(RegionFts, 2.0, 1.0)"
        for term in short_terms:
            conditions.append("instr(region.ContextText || ' ' || region.BodyText, ?) > 0")
            parameters.append(term)
        where = " AND ".join(conditions) if conditions else "1"
        sql = (
            f"select region.PageTitle, region.RegionOrdinal, {rank} as score "
            f"from {from_clause} where {where} order by score, region.PageTitle, region.RegionOrdinal limit 100"
        )
        timings = []
        rows = []
        for _ in range(15):
            started = time.perf_counter()
            rows = connection.execute(sql, parameters).fetchall()
            timings.append((time.perf_counter() - started) * 1000)
        count = connection.execute(f"select count(*) from {from_clause} where {where}", parameters).fetchone()[0]
        results.append(
            {
                "query": display,
                "candidate_count": count,
                "expected_page_top100_rank": next(
                    (index + 1 for index, row in enumerate(rows) if row[0] == expected), None
                ),
                "timing": summary(timings),
            }
        )
    connection.close()
    return results


def benchmark_encoded(index_path: Path, source: Path, query_encoder) -> list[dict[str, object]]:
    connection = sqlite3.connect(index_path)
    connection.execute("attach database ? as source", (str(source),))
    results = []
    for display, concepts, expected in QUERIES:
        expression = query_encoder(concepts)
        sql = (
            "select region.PageTitle, region.RegionOrdinal, bm25(RegionFts, 2.0, 1.0) as score "
            "from RegionFts join source.WikiSearchRegion as region on region.Id=RegionFts.rowid "
            "where RegionFts match ? order by score, region.PageTitle, region.RegionOrdinal limit 100"
        )
        timings = []
        rows = []
        for _ in range(15):
            started = time.perf_counter()
            rows = connection.execute(sql, (expression,)).fetchall()
            timings.append((time.perf_counter() - started) * 1000)
        count = connection.execute(
            "select count(*) from RegionFts where RegionFts match ?", (expression,)
        ).fetchone()[0]
        results.append(
            {
                "query": display,
                "candidate_count": count,
                "expected_page_top100_rank": next(
                    (index + 1 for index, row in enumerate(rows) if row[0] == expected), None
                ),
                "timing": summary(timings),
            }
        )
    connection.close()
    return results


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output-directory", required=True, type=Path)
    parser.add_argument("--result", required=True, type=Path)
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    output_directory = args.output_directory.resolve()
    output_directory.mkdir(parents=True, exist_ok=True)
    trigram_path = output_directory / "trigram-index.db"
    current_path = output_directory / "character-position-index.db"
    bigram_path = output_directory / "unigram-bigram-index.db"

    trigram_seconds, trigram_bytes = build_trigram(source, trigram_path)
    current_seconds, current_bytes = build_encoded(source, current_path, encode_current)
    bigram_seconds, bigram_bytes = build_encoded(source, bigram_path, encode_unigram_bigram)
    source_connection = sqlite3.connect(f"file:{source}?mode=ro", uri=True)
    region_count = source_connection.execute(
        "select count(*) from WikiSearchRegion"
    ).fetchone()[0]
    current_blocks = source_connection.execute(
        "select coalesce(sum(length(block)), 0) from WikiSearchRegionFts_data"
    ).fetchone()[0]
    current_docsize = source_connection.execute(
        "select coalesce(sum(length(sz)), 0) from WikiSearchRegionFts_docsize"
    ).fetchone()[0]
    source_connection.close()

    report = {
        "captured_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "source": str(source),
        "region_count": region_count,
        "current_character_position_index": {
            "fts_payload_bytes": current_blocks + current_docsize,
            "standalone_database_bytes": current_bytes,
            "build_seconds": current_seconds,
            "queries": benchmark_encoded(current_path, source, current_query),
            "full_query_metrics": "real-query-results-v2.json",
        },
        "trigram_with_short_scan": {
            "build_seconds": trigram_seconds,
            "database_bytes": trigram_bytes,
            "queries": benchmark_trigram(trigram_path, source),
        },
        "unigram_bigram_candidate": {
            "build_seconds": bigram_seconds,
            "database_bytes": bigram_bytes,
            "queries": benchmark_encoded(bigram_path, source, bigram_query),
        },
        "notes": [
            "Alternative results measure same-region candidate recall and lookup only, not the production hierarchy/alias ranking pipeline.",
            "Trigram queries shorter than three Han characters use a full WikiSearchRegion text scan.",
        ],
    }
    args.result.resolve().parent.mkdir(parents=True, exist_ok=True)
    args.result.resolve().write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
