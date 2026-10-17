namespace Game.Sim.Relationships
{
    /// <summary>
    /// A keepsake from a great date: who, when, where, and how the two of them looked that night. The looks are kept
    /// as DNA JSON, because residents change their looks over the weeks and the photo shouldn't.
    /// </summary>
    public sealed class Photo
    {
        public int Resident;
        public int Day;
        public string Location;
        public string PlayerLook;
        public string TheirLook;
    }
}
