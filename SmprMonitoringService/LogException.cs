﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmprMonitoringService
{
    public class LogException : Exception
    {
        public LogException(string message) : base(message) { }
    }
}
