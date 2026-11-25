using System;

namespace NOReplay
{
    public static class RecordGeo
    {
        public static (float, float) CartesianToGeodetic(float U /* X */, float V /* Z */)
        {
            //Stupid simplification but it works.
            float longArc = (float)Math.PI * 6378137;
            float latArc = longArc / 2;

            float latitude = V * 90 / latArc;
            float longitude = U * 180 / longArc;

            return (latitude, longitude);
        }
    }
}
