// SPDX-License-Identifier: GPL-3.0-or-later
using System.ServiceProcess;

namespace VictusHub.Service;
internal static class Program
{
    private static void Main() => ServiceBase.Run(new ControlService());
}
