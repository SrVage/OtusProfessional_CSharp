using Common;

namespace OtusProfessional_CSharp.Test;

public class BinarySerializerTests
{
    [Fact]
    public void Weapon_Roundtrip_PreservesAllFields()
    {
        var original = new Weapon { Name = "Excalibur", Damage = 42 };

        var bytes = original.SerializeToBinary();
        var restored = Weapon.DeserializeFromBinary(bytes);

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Damage, restored.Damage);
    }

    [Fact]
    public void UserProfile_WithNestedWeapon_Roundtrip_PreservesEverything()
    {
        var original = new UserProfile
        {
            Id = 7,
            Username = "kramp",
            CreatedAt = new DateTime(2026, 5, 21, 10, 30, 0, DateTimeKind.Utc),
            Weapon = new Weapon { Name = "Mjolnir", Damage = 9001 }
        };

        var bytes = original.SerializeToBinary();
        var restored = UserProfile.DeserializeFromBinary(bytes);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Username, restored.Username);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.NotNull(restored.Weapon);
        Assert.Equal(original.Weapon.Name, restored.Weapon.Name);
        Assert.Equal(original.Weapon.Damage, restored.Weapon.Damage);
    }

    [Fact]
    public void UserProfile_WithNullWeapon_Roundtrip_KeepsWeaponNull()
    {
        var original = new UserProfile
        {
            Id = 1,
            Username = "noweapon",
            CreatedAt = DateTime.UnixEpoch,
            Weapon = null!
        };

        var bytes = original.SerializeToBinary();
        var restored = UserProfile.DeserializeFromBinary(bytes);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Username, restored.Username);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Null(restored.Weapon);
    }
}
