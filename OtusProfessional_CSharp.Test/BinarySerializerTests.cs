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
    public void Deserialize_NullArray_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => UserProfile.DeserializeFromBinary(null!));
    }

    [Fact]
    public void Deserialize_TooShortArray_ThrowsInvalidData()
    {
        //3 байта — даже Id (int32) не вычитать
        Assert.Throws<System.IO.InvalidDataException>(() => UserProfile.DeserializeFromBinary(new byte[3]));
    }

    [Fact]
    public void Deserialize_GarbageBytes_ThrowsInvalidData()
    {
        //Случайные байты: длина строки прочитается как огромная и упадёт на Slice
        var garbage = Enumerable.Range(0, 64).Select(i => (byte)(i * 31)).ToArray();
        Assert.Throws<System.IO.InvalidDataException>(() => UserProfile.DeserializeFromBinary(garbage));
    }

    [Fact]
    public void Deserialize_TruncatedAfterValidPrefix_ThrowsInvalidData()
    {
        var original = new UserProfile
        {
            Id = 1, Username = "kramp", CreatedAt = DateTime.UnixEpoch,
            Weapon = new Weapon { Name = "Sword", Damage = 10 }
        };
        var bytes = original.SerializeToBinary();
        //Обрезаем середину — Username вычитается, на CreatedAt не хватит
        var truncated = bytes.AsSpan(0, 10).ToArray();
        Assert.Throws<System.IO.InvalidDataException>(() => UserProfile.DeserializeFromBinary(truncated));
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
