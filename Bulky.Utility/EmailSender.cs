﻿using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using SendGrid.Helpers.Mail;
using SendGrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bulky.Utility
{
    public class EmailSender : IEmailSender
    {
        public string SendGridSecret { get; set; }

        public EmailSender(IConfiguration _config)
        {
            // read key from appsettings.json
            SendGridSecret = _config.GetValue<string>("SendGrid:SecretKey");
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            // logic to send email
            var client = new SendGridClient(SendGridSecret);

            var from = new EmailAddress("kengchiachang@gmail.com", "BulkyWeb");
            var to = new EmailAddress(email);
            // htmlMessage is email containing html format
            var message = MailHelper.CreateSingleEmail(from, to, subject, "", htmlMessage);

            return client.SendEmailAsync(message);
        }

    }
}
