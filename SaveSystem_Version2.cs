using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class PlayerProfile
{
    public string playerId;               // Photon userId or device id
    public string playerName;             // Nickname/username
    public int playerLevel = 1;
    public int extraLives = 0;            // permanent lives purchased
    public int metaCurrency = 0;          // banked currency across runs
    public Dictionary<string, int> upgrades = new(); // statName -> ranks

    public int Get(string key) => (upgrades != null && upgrades.TryGetValue(key, out var v)) ? v : 0;
    public void Add(string key, int delta = 1)
    {
        if (upgrades == null) upgrades = new();
        if (!upgrades.ContainsKey(key)) upgrades[key] = 0;
        upgrades[key] += delta;
    }
}

// JsonUtility can’t serialize Dictionary. Use DTO with a serializable list.
[Serializable]
class UpgradeKV { public string key; public int value; }

[Serializable]
class PlayerProfileDTO
{
    public string playerId;
    public string playerName;
    public int playerLevel;
    public int extraLives;
    public int metaCurrency;
    public List<UpgradeKV> upgrades;
}

public static class SaveSystem
{
    private static string Dir => Path.Combine(Application.persistentDataPath, "Profiles");
    private static string PathFor(string id) => System.IO.Path.Combine(Dir, id + ".json");

    public static PlayerProfile Load(string id)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string p = PathFor(id);
            if (!File.Exists(p))
                return NewDefault(id);

            var json = File.ReadAllText(p);
            var dto = JsonUtility.FromJson<PlayerProfileDTO>(json);
            if (dto == null)
                return NewDefault(id);

            var prof = FromDto(dto, id);
            if (prof.playerLevel <= 0) prof.playerLevel = 1;
            if (prof.upgrades == null) prof.upgrades = new();
            return prof;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Save load failed: " + e.Message);
            return NewDefault(id);
        }
    }

    public static void Save(PlayerProfile profile)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var dto = ToDto(profile);
            var json = JsonUtility.ToJson(dto, prettyPrint: false);
            File.WriteAllText(PathFor(profile.playerId), json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Save write failed: " + e.Message);
        }
    }

    private static PlayerProfile NewDefault(string id)
    {
        return new PlayerProfile
        {
            playerId = id,
            playerLevel = 1,
            upgrades = new Dictionary<string, int>()
        };
    }

    private static PlayerProfileDTO ToDto(PlayerProfile p)
    {
        var dto = new PlayerProfileDTO
        {
            playerId = p.playerId,
            playerName = p.playerName,
            playerLevel = p.playerLevel,
            extraLives = p.extraLives,
            metaCurrency = p.metaCurrency,
            upgrades = new List<UpgradeKV>()
        };

        if (p.upgrades != null)
        {
            foreach (var kv in p.upgrades)
                dto.upgrades.Add(new UpgradeKV { key = kv.Key, value = kv.Value });
        }
        return dto;
    }

    private static PlayerProfile FromDto(PlayerProfileDTO dto, string fallbackId)
    {
        var prof = new PlayerProfile
        {
            playerId = string.IsNullOrEmpty(dto.playerId) ? fallbackId : dto.playerId,
            playerName = dto.playerName,
            playerLevel = dto.playerLevel,
            extraLives = dto.extraLives,
            metaCurrency = dto.metaCurrency,
            upgrades = new Dictionary<string, int>()
        };

        if (dto.upgrades != null)
        {
            for (int i = 0; i < dto.upgrades.Count; i++)
            {
                var kv = dto.upgrades[i];
                if (!string.IsNullOrEmpty(kv.key))
                    prof.upgrades[kv.key] = kv.value;
            }
        }
        return prof;
    }
}