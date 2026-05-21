using System.Text;
namespace Common {
public partial class UserProfile {
public byte[] SerializeToBinary() {
int totalSize = 0;
int UsernameLength = Encoding.UTF8.GetByteCount(Username ?? string.Empty);
totalSize += (4 + UsernameLength);
byte[] WeaponBytes = Weapon?.SerializeToBinary() ?? Array.Empty<byte>();
int WeaponLength = Weapon is null ? -1 : WeaponBytes.Length;
totalSize += 4 + (WeaponLength < 0 ? 0 : WeaponLength);
int totalBytesCount = 12;
totalBytesCount += totalSize;
byte[] array = new byte[totalBytesCount];
int offset = 0;
Span<byte> span = new Span<byte>(array);
//Serialize Id of type int
BitConverter.TryWriteBytes(span.Slice(offset, 4), Id);
offset += 4;
//Serialize Username of type string
BitConverter.TryWriteBytes(span.Slice(offset, 4), UsernameLength);
offset += 4;
Encoding.UTF8.GetBytes(Username ?? string.Empty, span.Slice(offset));
offset += UsernameLength;
//Serialize CreatedAt of type System.DateTime
BitConverter.TryWriteBytes(span.Slice(offset, 8), CreatedAt.ToBinary());
offset += 8;
//Serialize Weapon of type Common.Weapon
BitConverter.TryWriteBytes(span.Slice(offset, 4), WeaponLength);
offset += 4;
if (WeaponLength > 0) {
WeaponBytes.CopyTo(span.Slice(offset, WeaponLength));
offset += WeaponLength;
}
return array;
}
public static UserProfile DeserializeFromBinary(byte[] array) {
var result = new UserProfile();
int offset = 0;
ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(array);
//Deserialize Id of type int
result.Id = BitConverter.ToInt32(span.Slice(offset, 4));
offset += 4;
//Deserialize Username of type string
int UsernameLength = BitConverter.ToInt32(span.Slice(offset, 4));
offset += 4;
result.Username = Encoding.UTF8.GetString(span.Slice(offset, UsernameLength));
offset += UsernameLength;
//Deserialize CreatedAt of type System.DateTime
result.CreatedAt = DateTime.FromBinary(BitConverter.ToInt64(span.Slice(offset, 8)));
offset += 8;
//Deserialize Weapon of type Common.Weapon
int WeaponLength = BitConverter.ToInt32(span.Slice(offset, 4));
offset += 4;
if (WeaponLength < 0) {
result.Weapon = null;
}
else {
result.Weapon = Common.Weapon.DeserializeFromBinary(span.Slice(offset, WeaponLength).ToArray());
offset += WeaponLength;
}
return result;
}
}
}