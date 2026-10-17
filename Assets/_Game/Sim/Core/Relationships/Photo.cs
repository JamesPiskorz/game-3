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

        /// <summary>Their first name and the place's name, for captions where the town isn't loaded (the main menu).</summary>
        public string Name, Place;
        public string TheirLook;
    }
}
