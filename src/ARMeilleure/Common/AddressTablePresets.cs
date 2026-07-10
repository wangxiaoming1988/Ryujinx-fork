namespace ARMeilleure.Common
{
    public static class AddressTablePresets
    {
        private static readonly AddressTableLevel[] _levels64Bit =
        [
            new(31, 17),
                new(23,  8),
                new(15,  8),
                new( 7,  8),
                new( 2,  5)
        ];

        private static readonly AddressTableLevel[] _levels32Bit =
        [
            new(31, 17),
                new(23,  8),
                new(15,  8),
                new( 7,  8),
                new( 1,  6)
        ];
        
        private static readonly AddressTableLevel[] _monoSparse64Bit =
        [
            new( 2, 37)
        ];

        private static readonly AddressTableLevel[] _monoSparse32Bit =
        [
            new( 1, 31)
        ];
        
        public static AddressTableLevel[] GetArmPreset(bool for64Bits, bool sparse)
        {
            if (sparse)
            {
                return for64Bits ? _monoSparse64Bit : _monoSparse32Bit;
            }
            else
            {
                return for64Bits ? _levels64Bit : _levels32Bit;
            }
        }
    }
}
