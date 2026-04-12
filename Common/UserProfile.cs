namespace Common;

[Serializable]
public class UserProfile
{
    public int Id { get; set; }
    public string Username { get; set; }
    public DateTime CreatedAt { get; set; }
}