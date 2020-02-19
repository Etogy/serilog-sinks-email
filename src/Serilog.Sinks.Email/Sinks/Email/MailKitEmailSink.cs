// Copyright 2014 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if MAIL_KIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;
using MailKit.Net.Smtp;
using System.Threading.Tasks;

namespace Serilog.Sinks.Email
{
    class EmailSink : IBatchedLogEventSink, ILogEventSink
    {
        readonly EmailConnectionInfo _connectionInfo;

        readonly MimeKit.InternetAddress _fromAddress;
        readonly IEnumerable<MimeKit.InternetAddress> _toAddresses;

        readonly ITextFormatter _textFormatter;

        readonly ITextFormatter _subjectFormatter;

        private static Task CompletedTask = Task.FromResult(false); // The value is irrelevant as it just marks a completed operation.

        /// <summary>
        /// Construct a sink emailing with the specified details.
        /// </summary>
        /// <param name="connectionInfo">Connection information used to construct the SMTP client and mail messages.</param>
        /// <param name="textFormatter">Supplies culture-specific formatting information, or null.</param>
        /// <param name="subjectLineFormatter">The subject line formatter.</param>
        /// <exception cref="System.ArgumentNullException">connectionInfo</exception>
        public EmailSink(EmailConnectionInfo connectionInfo, ITextFormatter textFormatter, ITextFormatter subjectLineFormatter)
        {
            if (connectionInfo == null) throw new ArgumentNullException(nameof(connectionInfo));

            _connectionInfo = connectionInfo;
            _fromAddress = MimeKit.MailboxAddress.Parse(_connectionInfo.FromEmail);
            _toAddresses = connectionInfo
                .ToEmail
                .Split(",;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                .Select(MimeKit.MailboxAddress.Parse)
                .ToArray();

            _textFormatter = textFormatter;
            _subjectFormatter = subjectLineFormatter;
        }
        
        private MimeKit.MimeMessage CreateMailMessage(string payload, string subject)
        {
            var mailMessage = new MimeKit.MimeMessage();
            mailMessage.From.Add(_fromAddress);
            mailMessage.To.AddRange(_toAddresses);
            mailMessage.Subject = subject;
            mailMessage.Body = _connectionInfo.IsBodyHtml
                ? new MimeKit.BodyBuilder { HtmlBody = payload }.ToMessageBody()
                : new MimeKit.BodyBuilder { TextBody = payload }.ToMessageBody();
            return mailMessage;            
        }

        /// <summary>
        /// Emit the provided log event to the sink.
        /// </summary>
        /// <param name="logEvent">The log event to write.</param>
        public void Emit(LogEvent logEvent)
        {
            SelfLog.WriteLine("The email sink only supports batched log events.");
        }

        /// <summary>
        /// Emit a batch of log events, running asynchronously.
        /// </summary>
        /// <param name="events">The events to emit.</param>
        public async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {
            if (events == null)
                throw new ArgumentNullException(nameof(events));

            var payload = new StringWriter();

            foreach (var logEvent in events)
            {
                _textFormatter.Format(logEvent, payload);
            }

            var subject = new StringWriter();
            _subjectFormatter.Format(events.OrderByDescending(e => e.Level).First(), subject);

            var mailMessage = CreateMailMessage(payload.ToString(), subject.ToString());

            try
            {
                using (var smtpClient = OpenConnectedSmtpClient())
                {
                    await smtpClient.SendAsync(mailMessage);
                    await smtpClient.DisconnectAsync(quit: true);
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Failed to send email: {0}", ex.ToString());
            }
        }

        /// <summary>
        /// Allows sinks to perform periodic work without requiring additional threads
        /// or timers (thus avoiding additional flush/shut-down complexity).
        /// </summary>
        /// <returns></returns>
        public Task OnEmptyBatchAsync()
        {
            return CompletedTask;
        }

        private SmtpClient OpenConnectedSmtpClient()
        {
            var smtpClient = new SmtpClient();
            if (!string.IsNullOrWhiteSpace(_connectionInfo.MailServer))
            {
                if (_connectionInfo.ServerCertificateValidationCallback != null)
                {
                    smtpClient.ServerCertificateValidationCallback += _connectionInfo.ServerCertificateValidationCallback;
                }

                smtpClient.Connect(
                    _connectionInfo.MailServer, _connectionInfo.Port,
                    useSsl: _connectionInfo.EnableSsl);

                if (_connectionInfo.NetworkCredentials != null)
                {
                    smtpClient.Authenticate(
                        Encoding.UTF8,
                        _connectionInfo.NetworkCredentials.GetCredential(
                            _connectionInfo.MailServer, _connectionInfo.Port, "smtp"));
                }
            }
            return smtpClient;
        }
    }
}
#endif