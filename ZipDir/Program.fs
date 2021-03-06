﻿open Ionic.Zip
open System;
open System.Net
open System.Net.Mail
open System.IO
open FSharp.Configuration
open FSharp.Data

type Settings = AppSettings<"App.config">
// open System.Configuration
let InputDir = Settings.InputDir
let OutputDir = Settings.OutputDir
let isSSL = Settings.SSL

let attachmentName = Settings.AttachmentName

[<Literal>]
let connectionString = @"name=HRMS"


let getUserPassword (empcode : string) :string = 
    let mutable result = ""
    do
        use cmd = new SqlCommandProvider<"SELECT TOP(1) userpassword from users u INNER JOIN emphr e on e.empid = u.empid WHERE e.empcode = @empcode",connectionString>()
        
        let users = cmd.Execute(empcode = empcode)

        for user in users do
            result <- user
    
    result

type EmailSettings = {smtpserver:string; smtpport:int; smtpsender:string; smtpuser : string; smtppassword : string}

let getEmailSettings = 
    let mutable result =  {smtpserver = ""; smtpport = 0; smtpsender = ""; smtpuser = ""; smtppassword = "" }
    do
        use cmd = new SqlCommandProvider<"SELECT TOP(1) smtpserver, smtpport, smtpsender, smtpuser, smtppassword from control",connectionString>()
        
        let controls = cmd.Execute()

        for control in controls do
            result <- {smtpserver = control.smtpserver; smtpport = System.Int32.Parse(control.smtpport); smtpsender = control.smtpsender; smtpuser = control.smtpuser; smtppassword = control.smtppassword }

    result

let getEmailAddress (empcode : string) :string = 
    let mutable result = ""
    do
        use cmd = new SqlCommandProvider<"SELECT TOP(1) cemail from emphr WHERE empcode = @empcode",connectionString>()
        
        let employees = cmd.Execute(empcode = empcode)

        for employee in employees do
            result <- employee
    
    result

let sendMailMessage( email :string, name :string, topic :string, msg :string, filename :string) =

    let emailSettings = getEmailSettings
    let msg = 
        new MailMessage(
            emailSettings.smtpsender, email, topic, "Dear " + name + ", <br/><br/>\r\n\r\n" + msg)
    msg.IsBodyHtml <- true
    let client = new SmtpClient(emailSettings.smtpserver, emailSettings.smtpport)
    client.EnableSsl <- isSSL
    client.Credentials <- System.Net.NetworkCredential(emailSettings.smtpuser, emailSettings.smtppassword)
    client.SendCompleted |> Observable.add(fun e -> 
        let msg = e.UserState :?> MailMessage
        if e.Cancelled then
            ("Mail message cancelled:\r\n" + msg.Subject) |> Console.WriteLine
        if e.Error <> null then
            ("Sending mail failed for message:\r\n" + msg.Subject + 
                ", reason:\r\n" + e.Error.ToString()) |> Console.WriteLine
        if msg<>Unchecked.defaultof<MailMessage> then msg.Dispose()
        if client<>Unchecked.defaultof<SmtpClient> then client.Dispose()
    )
    
    let data = new System.Net.Mail.Attachment(filename,  System.Net.Mime.MediaTypeNames.Application.Octet)
    data.Name <- attachmentName
    msg.Attachments.Add data
    let disposition = data.ContentDisposition
    disposition.CreationDate <- System.IO.File.GetCreationTime(filename)
    disposition.ModificationDate <- System.IO.File.GetLastWriteTime(filename)
    disposition.ReadDate <- System.IO.File.GetLastAccessTime(filename)
    // Maybe some System.Threading.Thread.Sleep to prevent mail-server hammering
    client.Send(msg)

let getZipFileName( filename : string ) =
    let index1 = filename.LastIndexOf("\\") + 1
    let index2 = filename.LastIndexOf(".pdf")
    filename.Substring( index1, index2 - index1)

let zipFiles(zipFolder: string, unzipFolder: string) =

    let files =  Directory.EnumerateFiles (unzipFolder)
    for file in files do
        use zip1 = new ZipFile()
        let empcode = getZipFileName file
        let mutable password = getUserPassword(empcode)
        if password.Length = 0 then
            password <- "123456"
        zip1.set_Password(password)
        zip1.AddFile(file, "") |> ignore
        
        
        let zipFileName = sprintf "%s/%s.zip" zipFolder empcode
        File.Delete(zipFileName)
        zip1.Save(zipFileName)

        let emailaddress = getEmailAddress(empcode)
        sendMailMessage( emailaddress, "the name", "the topic", "the message", zipFileName)
        zip1.Dispose()

[<EntryPoint>]
let main argv = 
    //let path = Directory.GetCurrentDirectory()

    //Console.WriteLine("The current directory is {0}", path)
    zipFiles( OutputDir, InputDir)
    0 // return an integer exit code
