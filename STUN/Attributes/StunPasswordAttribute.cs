using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STUN.Attributes
{
    public class STUNPasswordAttribute : STUNAnsiTextAttribute
    {
        public override string ToString()
        {
            return string.Format("PASSWORD \"{0}\"", Text);
        }
    }
}
