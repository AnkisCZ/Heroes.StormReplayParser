﻿using Heroes.StormReplayParser.MpqFiles;
using Heroes.StormReplayParser.MpqHeroesTool;
using Heroes.StormReplayParser.Replay;
using System;
using System.Buffers;
using System.Collections;
using System.Linq;
using System.Security.Principal;

namespace Heroes.StormReplayParser
{
    /// <summary>
    /// Contains the information to parse a Heroes of the Storm replay.
    /// </summary>
    public partial class StormReplay
    {
        private static StormReplayParseStatus _stormReplayParseResult = StormReplayParseStatus.Incomplete;
        private static Exception? _failedReplayException = null;

        private readonly string _fileName;
        private readonly bool _allowPTRRegion;
        private readonly bool _parseBattleLobby;
        private readonly MpqHeroesArchive _stormMpqArchive;

        private StormReplay(string fileName, bool allowPTRRegion, bool parseBattleLobby)
        {
            _fileName = fileName;
            _allowPTRRegion = allowPTRRegion;
            _parseBattleLobby = parseBattleLobby;

            _stormMpqArchive = MpqHeroesFile.Open(_fileName);
        }

        /// <summary>
        /// Parses a .StormReplay file.
        /// </summary>
        /// <param name="fileName">The file name which may contain the path.</param>
        /// <param name="allowPTRRegion">If false, the result status will be <see cref="StormReplayParseStatus.PTRRegion"/> if the replay is successfully parsed.</param>
        /// <param name="parseBattleLobby">If enabled, the battle lobby file will be parsed which gives more available data.</param>
        /// <returns>A <see cref="StormReplayResult"/>.</returns>
        public static StormReplayResult Parse(string fileName, bool allowPTRRegion = false, bool parseBattleLobby = true)
        {
            StormReplay stormReplay = ParseStormReplay(fileName, allowPTRRegion, parseBattleLobby);

            if (_failedReplayException != null)
                return new StormReplayResult(stormReplay, _stormReplayParseResult, _failedReplayException);
            else
                return new StormReplayResult(stormReplay, _stormReplayParseResult);
        }

        private static StormReplay ParseStormReplay(string fileName, bool allowPTRRegion, bool parseBattleLobby)
        {
            StormReplay stormReplay = new StormReplay(fileName, allowPTRRegion, parseBattleLobby);

            try
            {
                stormReplay.Parse(stormReplay);
            }
            catch (Exception exception)
            {
                _failedReplayException = exception;
                _stormReplayParseResult = StormReplayParseStatus.Exception;
            }

            return stormReplay;
        }

        private void Parse(StormReplay stormReplay)
        {
            _stormMpqArchive.AddListfileFileNames();

            Span<byte> headerBuffer = stackalloc byte[MpqHeroesArchive.HeaderSize];
            _stormMpqArchive.GetHeaderBytes(headerBuffer);
            StormReplayHeader.Parse(stormReplay, headerBuffer);

            if (stormReplay.ReplayBuild < 32455)
            {
                _stormReplayParseResult = StormReplayParseStatus.PreAlphaWipe;
                return;
            }

            ArrayPool<byte> pool = ArrayPool<byte>.Shared;

            ParseReplayDetails(stormReplay, pool);

            if (stormReplay.Players.Length != 10 || stormReplay.Players.Count(i => i.IsWinner) != 5)
            {
                // Filter out 'Try Me' games, any games without 10 players, and incomplete games
                return;
            }
            else if (stormReplay.Timestamp == DateTime.MinValue)
            {
                // Uncommon issue when parsing replay.details
                return;
            }
            else if (stormReplay.Timestamp < new DateTime(2014, 10, 6, 0, 0, 0, DateTimeKind.Utc))
            {
                // Technical Alpha replays
                return;
            }

            ParseReplayInit(stormReplay, pool);
            ParseReplayAttributeEvents(stormReplay, pool);
            ParseReplayTrackerEvents(stormReplay, pool);
            ParseReplayMessageEvents(stormReplay, pool);

            if (_parseBattleLobby)
                ParseReplayServerBattlelobby(stormReplay, pool);

            ValidateResult(stormReplay);

            stormReplay.SetStormPlayerData();

            _stormMpqArchive.Dispose();
        }

        private void ParseReplayDetails(StormReplay stormReplay, ArrayPool<byte> pool)
        {
            MpqHeroesArchiveEntry entry = _stormMpqArchive.GetEntry(ReplayDetails.FileName);
            int size = (int)entry.FileSize;
            byte[] poolBuffer = pool.Rent(size);
            Span<byte> buffer = new Span<byte>(poolBuffer).Slice(0, size);
            _stormMpqArchive.DecompressEntry(entry, buffer);
            ReplayDetails.Parse(stormReplay, buffer);

            pool.Return(poolBuffer);
        }

        private void ParseReplayInit(StormReplay stormReplay, ArrayPool<byte> pool)
        {
            MpqHeroesArchiveEntry entry = _stormMpqArchive.GetEntry(ReplayInitData.FileName);
            int size = (int)entry.FileSize;
            byte[] poolBuffer = pool.Rent(size);
            Span<byte> buffer = new Span<byte>(poolBuffer).Slice(0, size);
            _stormMpqArchive.DecompressEntry(entry, buffer);
            ReplayInitData.Parse(stormReplay, buffer);

            pool.Return(poolBuffer);
        }

        private void ParseReplayAttributeEvents(StormReplay stormReplay, ArrayPool<byte> pool)
        {
            MpqHeroesArchiveEntry entry = _stormMpqArchive.GetEntry(ReplayAttributeEvents.FileName);
            int size = (int)entry.FileSize;
            byte[] poolBuffer = pool.Rent(size);
            Span<byte> buffer = new Span<byte>(poolBuffer).Slice(0, size);
            _stormMpqArchive.DecompressEntry(entry, buffer);
            ReplayAttributeEvents.Parse(stormReplay, buffer);

            pool.Return(poolBuffer);
        }

        private void ParseReplayTrackerEvents(StormReplay stormReplay, ArrayPool<byte> pool)
        {
            MpqHeroesArchiveEntry entry = _stormMpqArchive.GetEntry(ReplayTrackerEvents.FileName);
            int size = (int)entry.FileSize;
            byte[] poolBuffer = pool.Rent(size);
            Span<byte> buffer = new Span<byte>(poolBuffer).Slice(0, size);
            _stormMpqArchive.DecompressEntry(entry, buffer);
            ReplayTrackerEvents.Parse(stormReplay, buffer);

            pool.Return(poolBuffer);
        }

        private void ParseReplayMessageEvents(StormReplay stormReplay, ArrayPool<byte> pool)
        {
            MpqHeroesArchiveEntry entry = _stormMpqArchive.GetEntry(ReplayMessageEvents.FileName);
            int size = (int)entry.FileSize;
            byte[] poolBuffer = pool.Rent(size);
            Span<byte> buffer = new Span<byte>(poolBuffer).Slice(0, size);
            _stormMpqArchive.DecompressEntry(entry, buffer);
            ReplayMessageEvents.Parse(stormReplay, buffer);

            pool.Return(poolBuffer);
        }

        private void ParseReplayServerBattlelobby(StormReplay stormReplay, ArrayPool<byte> pool)
        {
            MpqHeroesArchiveEntry entry = _stormMpqArchive.GetEntry(ReplayServerBattlelobby.FileName);
            int size = (int)entry.FileSize;
            byte[] poolBuffer = pool.Rent(size);
            Span<byte> buffer = new Span<byte>(poolBuffer).Slice(0, size);
            _stormMpqArchive.DecompressEntry(entry, buffer);
            ReplayServerBattlelobby.Parse(stormReplay, buffer);

            pool.Return(poolBuffer);
        }

        private void ValidateResult(StormReplay stormReplay)
        {
            if (stormReplay.PlayersCount == 1)
                _stormReplayParseResult = StormReplayParseStatus.TryMeMode;
            else if (stormReplay.Players.All(x => !x.IsWinner) || stormReplay.ReplayLength.Minutes < 2)
                _stormReplayParseResult = StormReplayParseStatus.Incomplete;
            else if (stormReplay.Timestamp == DateTime.MinValue)
                _stormReplayParseResult = StormReplayParseStatus.UnexpectedResult;
            else if (stormReplay.Timestamp < new DateTime(2014, 10, 6, 0, 0, 0, DateTimeKind.Utc))
                _stormReplayParseResult = StormReplayParseStatus.PreAlphaWipe;
            else if (!_allowPTRRegion && stormReplay.Players.Any(x => x.ToonHandle.Region >= 90))
                _stormReplayParseResult = StormReplayParseStatus.PTRRegion;
            else if (stormReplay.Players.Count(x => x.IsWinner) != 5 || stormReplay.PlayersCount != 10 || !GameMode.AllGameModes.HasFlag(stormReplay.GameMode))
                _stormReplayParseResult = StormReplayParseStatus.UnexpectedResult;
            else
                _stormReplayParseResult = StormReplayParseStatus.Success;
        }
    }
}
