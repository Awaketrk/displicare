namespace Displicare.Rooms
{
    /// <summary>Room switching writes to Displicare's app.log, prefixed so its lines are easy to find.</summary>
    internal static class RoomLog
    {
        public static void Write(string message)
        {
            App.Log("Rooms: " + message);
        }
    }
}
