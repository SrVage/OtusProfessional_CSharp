using System.Text;
namespace Common {
public partial class Weapon {
public byte[] SerializeToBinary() {
int totalSize = 0;
int NameLength = Encoding.UTF8.GetByteCount(Name ?? string.Empty);
totalSize += (4 + NameLength);
int totalBytesCount = 4;
totalBytesCount += totalSize;
byte[] array = new byte[totalBytesCount];
int offset = 0;
Span<byte> span = new Span<byte>(array);
//Serialize Name of type string
BitConverter.TryWriteBytes(span.Slice(offset, 4), NameLength);
offset += 4;
Encoding.UTF8.GetBytes(Name ?? string.Empty, span.Slice(offset));
offset += NameLength;
//Serialize Damage of type int
BitConverter.TryWriteBytes(span.Slice(offset, 4), Damage);
offset += 4;
return array;
}
public static Weapon DeserializeFromBinary(byte[] array) {
if (array is null) throw new ArgumentNullException(nameof(array));
var result = new Weapon();
int offset = 0;
try {
ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(array);
//Deserialize Name of type string
int NameLength = BitConverter.ToInt32(span.Slice(offset, 4));
offset += 4;
result.Name = Encoding.UTF8.GetString(span.Slice(offset, NameLength));
offset += NameLength;
//Deserialize Damage of type int
result.Damage = BitConverter.ToInt32(span.Slice(offset, 4));
offset += 4;
return result;
}
catch (Exception ex) when (ex is not System.IO.InvalidDataException) {
throw new System.IO.InvalidDataException("Failed to deserialize Weapon at offset " + offset, ex);
}
}
}
}