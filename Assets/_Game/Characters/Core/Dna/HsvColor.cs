namespace Game.Characters.Dna
{
    /// <summary>
    /// A colour stored literally in the DNA, not as a palette reference, so editing a palette never
    /// recolours existing characters. Components are 0..1; hue wraps.
    /// </summary>
    public struct HsvColor
    {
        public float H;
        public float S;
        public float V;

        public HsvColor(float h, float s, float v)
        {
            H = h;
            S = s;
            V = v;
        }

        public override string ToString() => $"HSV({H:0.###}, {S:0.###}, {V:0.###})";
    }
}
