﻿namespace ServiceControl.Config.Validation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using FluentValidation;
    using ServiceControlInstaller.Engine.Ports;

    public static class Validations
    {
        public static IRuleBuilderOptions<T, string> ValidPort<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.Must((t, port) =>
                {
                    if (int.TryParse(port, out var result))
                    {
                        return result is >= 1 and <= 49151;
                    }

                    return false;
                })
                .WithMessage(MSG_USE_PORTS_IN_RANGE);
        }

        public static IRuleBuilderOptions<T, string> PortAvailable<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.Must((t, port) =>
                {
                    if (int.TryParse(port, out var result))
                    {
                        return PortUtils.CheckAvailable(result);
                    }

                    return false;
                })
                .WithMessage(MSG_PORT_IN_USE);
        }


        public static IRuleBuilderOptions<T, string> ValidPath<T>(this IRuleBuilder<T, string> rulebuilder)
        {
            return rulebuilder.Must((t, path) => { return path != null && !path.Intersect(ILLEGAL_PATH_CHARS).Any(); })
                .WithMessage(string.Format(MSG_ILLEGAL_PATH_CHAR, string.Join(" ", ILLEGAL_PATH_CHARS)));
        }

        public static IRuleBuilderOptions<T, TProperty> MustNotBeIn<T, TProperty>(this IRuleBuilder<T, TProperty> ruleBuilder, Func<T, IEnumerable<TProperty>> list) where TProperty : class
        {
            return ruleBuilder.Must((t, p) => p != null && !list(t).Contains(p));
        }

        public static IRuleBuilderOptions<T, string> MustNotContainWhitespace<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.Must(s => !string.IsNullOrEmpty(s) && !s.Any(c => char.IsWhiteSpace(c))).WithMessage(MSG_CANTCONTAINWHITESPACE);
        }

        public const string MSG_EMAIL_NOT_VALID = "Not Valid.";

        public const string MSG_THIS_TRANSPORT_REQUIRES_A_CONNECTION_STRING = "This transport requires a connection string.";
        public const string MSG_CANTCONTAINWHITESPACE = "Cannot contain white space.";

        public const string MSG_SELECTAUDITFORWARDING = "Must select audit forwarding.";

        public const string MSG_SELECTERRORFORWARDING = "Must select error forwarding.";

        public const string MSG_UNIQUEQUEUENAME = "Must not equal {0} queue name.";

        public const string MSG_USE_PORTS_IN_RANGE = "Use Ports in range 1 - 49151. Ephemeral port range should not be used (49152 to 65535).";

        public const string MSG_PORT_IN_USE = "The port specified is already in use by another process.";

        public const string MSG_MUST_BE_UNIQUE = "{0} must be unique across all instances";

        public const string MSG_QUEUE_ALREADY_ASSIGNED = "This queue is already assigned to another instance";

        public const string WRN_HOSTNAME_SHOULD_BE_LOCALHOST = "Not using localhost can expose ServiceControl to anonymous access.";

        public const string MSG_ILLEGAL_PATH_CHAR = "Paths cannot contain characters {0}";

        static char[] ILLEGAL_PATH_CHARS =
        {
            '*',
            '?',
            '"',
            '<',
            '>',
            '|'
        };
    }
}