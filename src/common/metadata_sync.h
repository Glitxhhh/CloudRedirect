#pragma once
#include <cstdint>
#include <atomic>

namespace MetadataSync {

extern std::atomic<bool> steamToolsPresent;
extern std::atomic<bool> syncLuas;

// Native stats/playtime sync gates (config: sync_achievements / sync_playtime).
extern std::atomic<bool> syncAchievements;
extern std::atomic<bool> syncPlaytime;

// Fetch missing achievement/stats schemas from the CM (config: schema_fetch).
extern std::atomic<bool> schemaFetch;

// Lifts the SteamTools-client gate on metadata features (config
// override_non_st_client_gate). Default false.
extern std::atomic<bool> overrideNonStGate;

inline bool IsEnabled() {
    return steamToolsPresent.load(std::memory_order_relaxed) &&
           syncLuas.load(std::memory_order_relaxed);
}

// The SteamTools-client gate. Windows-only; Linux always runs under SLSsteam.
inline bool StGateOpen() {
#if defined(__linux__)
    return true;
#else
    return steamToolsPresent.load(std::memory_order_relaxed) ||
           overrideNonStGate.load(std::memory_order_relaxed);
#endif
}

// Per-feature flag AND'd with the ST-gate.
inline bool AchievementsEnabled() {
    return syncAchievements.load(std::memory_order_relaxed) && StGateOpen();
}
inline bool PlaytimeEnabled() {
    return syncPlaytime.load(std::memory_order_relaxed) && StGateOpen();
}
inline bool SchemaFetchEnabled() {
    return schemaFetch.load(std::memory_order_relaxed) && StGateOpen();
}

}
