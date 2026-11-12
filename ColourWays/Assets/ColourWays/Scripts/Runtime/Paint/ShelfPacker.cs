using UnityEngine;

namespace ColourWays.Paint
{
    /// <summary>
    /// Minimal shelf rectangle packer used to lay out surface charts in the shared paint atlas.
    /// Rectangles should be fed tallest-first for decent packing.
    /// </summary>
    internal sealed class ShelfPacker
    {
        private readonly int _size;
        private int _shelfY;
        private int _shelfHeight;
        private int _cursorX;

        public ShelfPacker(int size) => _size = size;

        public bool TryPack(int width, int height, out RectInt rect)
        {
            rect = default;
            if (width > _size || height > _size)
                return false;

            if (_cursorX + width > _size)
            {
                _shelfY += _shelfHeight;
                _shelfHeight = 0;
                _cursorX = 0;
            }

            if (_shelfY + height > _size)
                return false;

            rect = new RectInt(_cursorX, _shelfY, width, height);
            _cursorX += width;
            if (height > _shelfHeight)
                _shelfHeight = height;
            return true;
        }
    }
}
