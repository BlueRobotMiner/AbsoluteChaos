using SQLite;
using UnityEngine;

public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance { get; private set; }

    SQLiteConnection _db;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        string path = Application.persistentDataPath + "/gamedata.db";
        _db = new SQLiteConnection(path);
        _db.CreateTable<CardRow>();

        if (_db.Table<CardRow>().Count() == 0)
            SeedCards();

        // Force-update descriptions that may have changed since initial seed
        _db.Execute("UPDATE Cards SET Description=? WHERE Id=?",
            "Guns you pick up carry a larger magazine than normal.", (int)CardId.AmmoStash);
    }

    void SeedCards()
    {
        var cards = new CardRow[]
        {
            // Bullet modifiers
            new() { Id = (int)CardId.ExplosiveRounds, Name = "Explosive Rounds", Description = "Your bullets explode on impact dealing area damage.",    Category = "Bullet",      Abbreviation = "EX" },
            new() { Id = (int)CardId.Ricochet,        Name = "Ricochet",         Description = "Your bullets bounce off walls once.",                    Category = "Bullet",      Abbreviation = "RC" },
            new() { Id = (int)CardId.RapidFire,       Name = "Rapid Fire",       Description = "Your fire rate is doubled for this round.",              Category = "Bullet",      Abbreviation = "RF" },

            // Spawn modifiers
            new() { Id = (int)CardId.HealthPackRain,  Name = "Health Pack Rain", Description = "Health packs begin spawning around the map.",            Category = "Spawn",       Abbreviation = "HP" },
            new() { Id = (int)CardId.AmmoStash,       Name = "Ammo Stash",       Description = "Ammo pickups start appearing around the map.",           Category = "Spawn",       Abbreviation = "AM" },

            // Player stat modifiers
            new() { Id = (int)CardId.SpeedBoost,      Name = "Speed Boost",      Description = "You move 50% faster this round.",                       Category = "Stat",        Abbreviation = "SP" },
            new() { Id = (int)CardId.DoubleJump,      Name = "Double Jump",      Description = "You can jump a second time while airborne.",             Category = "Stat",        Abbreviation = "DJ" },
            new() { Id = (int)CardId.Fragile,         Name = "Fragile",          Description = "All other players take 50% more damage this round.",     Category = "Stat",        Abbreviation = "FR" },

            // Environment modifiers
            new() { Id = (int)CardId.LowGravity,      Name = "Low Gravity",      Description = "Gravity is reduced for everyone this round.",            Category = "Environment", Abbreviation = "LG" },
            new() { Id = (int)CardId.HeavyGravity,    Name = "Heavy Gravity",    Description = "Gravity is increased for everyone this round.",          Category = "Environment", Abbreviation = "HG" },
        };

        _db.InsertAll(cards);
        Debug.Log("[CardDatabase] Seeded " + cards.Length + " cards.");
    }

    public CardData[] GetRandomCards(int count)
    {
        var all = _db.Table<CardRow>().ToList();

        for (int i = 0; i < all.Count; i++)
        {
            int j = Random.Range(i, all.Count);
            (all[i], all[j]) = (all[j], all[i]);
        }

        count = Mathf.Min(count, all.Count);
        var result = new CardData[count];
        for (int i = 0; i < count; i++)
            result[i] = ToCardData(all[i]);
        return result;
    }

    public CardData GetById(CardId id)
    {
        var row = _db.Find<CardRow>((int)id);
        return row != null ? ToCardData(row) : null;
    }

    CardData ToCardData(CardRow row)
    {
        return new CardData
        {
            id           = (CardId)row.Id,
            displayName  = row.Name,
            description  = row.Description,
            category     = row.Category,
            abbreviation = row.Abbreviation,
            icon         = CardIconRegistry.Instance?.GetIcon((CardId)row.Id),
        };
    }

    void OnDestroy()
    {
        _db?.Close();
    }
}

// SQLite table schema — sqlite-net-pcl maps this class to a table row
[Table("Cards")]
public class CardRow
{
    [PrimaryKey] public int    Id           { get; set; }
                 public string Name         { get; set; }
                 public string Description  { get; set; }
                 public string Category     { get; set; }
                 public string Abbreviation { get; set; }
}
