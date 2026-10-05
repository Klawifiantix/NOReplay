using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOReplay
{
    public class RecordTextWriter : StreamWriter
    {
        private readonly IFormatProvider formatProvider;

        public RecordTextWriter(string path, IFormatProvider formatProvider)
            : base(path)
        {
            this.formatProvider = formatProvider;
        }

        public override IFormatProvider FormatProvider
        {
            get
            {
                return this.formatProvider;
            }
        }
    }
}
