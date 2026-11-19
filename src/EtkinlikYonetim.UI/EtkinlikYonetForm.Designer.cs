namespace EtkinlikYonetim.UI
{
    partial class EtkinlikYonetForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblEtkinlikler = new System.Windows.Forms.Label();
            this.lstEtkinlikler = new System.Windows.Forms.ListBox();
            this.lblSecili = new System.Windows.Forms.Label();
            this.lblDoluluk = new System.Windows.Forms.Label();
            this.lblTarih = new System.Windows.Forms.Label();
            this.dtpTarih = new System.Windows.Forms.DateTimePicker();
            this.lblKapasite = new System.Windows.Forms.Label();
            this.numKapasite = new System.Windows.Forms.NumericUpDown();
            this.lblAciklama = new System.Windows.Forms.Label();
            this.txtAciklama = new System.Windows.Forms.TextBox();
            this.btnKaydet = new System.Windows.Forms.Button();
            this.btnIptalEt = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numKapasite)).BeginInit();
            this.SuspendLayout();
            //
            // lblEtkinlikler
            //
            this.lblEtkinlikler.AutoSize = true;
            this.lblEtkinlikler.Location = new System.Drawing.Point(12, 12);
            this.lblEtkinlikler.Name = "lblEtkinlikler";
            this.lblEtkinlikler.Size = new System.Drawing.Size(67, 16);
            this.lblEtkinlikler.TabIndex = 0;
            this.lblEtkinlikler.Text = "Etkinlikler";
            //
            // lstEtkinlikler
            //
            this.lstEtkinlikler.FormattingEnabled = true;
            this.lstEtkinlikler.HorizontalScrollbar = true;
            this.lstEtkinlikler.IntegralHeight = false;
            this.lstEtkinlikler.ItemHeight = 16;
            this.lstEtkinlikler.Location = new System.Drawing.Point(12, 34);
            this.lstEtkinlikler.Name = "lstEtkinlikler";
            this.lstEtkinlikler.Size = new System.Drawing.Size(330, 370);
            this.lstEtkinlikler.TabIndex = 1;
            this.lstEtkinlikler.SelectedIndexChanged += new System.EventHandler(this.lstEtkinlikler_SelectedIndexChanged);
            //
            // lblSecili
            //
            this.lblSecili.Location = new System.Drawing.Point(360, 34);
            this.lblSecili.Name = "lblSecili";
            this.lblSecili.Size = new System.Drawing.Size(350, 40);
            this.lblSecili.TabIndex = 2;
            this.lblSecili.Text = "Düzenlemek için soldan bir etkinlik seçin.";
            //
            // lblDoluluk
            //
            this.lblDoluluk.Location = new System.Drawing.Point(360, 76);
            this.lblDoluluk.Name = "lblDoluluk";
            this.lblDoluluk.Size = new System.Drawing.Size(350, 20);
            this.lblDoluluk.TabIndex = 3;
            //
            // lblTarih
            //
            this.lblTarih.AutoSize = true;
            this.lblTarih.Location = new System.Drawing.Point(360, 110);
            this.lblTarih.Name = "lblTarih";
            this.lblTarih.Size = new System.Drawing.Size(38, 16);
            this.lblTarih.TabIndex = 4;
            this.lblTarih.Text = "Tarih";
            //
            // dtpTarih
            //
            this.dtpTarih.Location = new System.Drawing.Point(460, 106);
            this.dtpTarih.Name = "dtpTarih";
            this.dtpTarih.Size = new System.Drawing.Size(250, 22);
            this.dtpTarih.TabIndex = 5;
            //
            // lblKapasite
            //
            this.lblKapasite.AutoSize = true;
            this.lblKapasite.Location = new System.Drawing.Point(360, 146);
            this.lblKapasite.Name = "lblKapasite";
            this.lblKapasite.Size = new System.Drawing.Size(59, 16);
            this.lblKapasite.TabIndex = 6;
            this.lblKapasite.Text = "Kapasite";
            //
            // numKapasite
            //
            this.numKapasite.Location = new System.Drawing.Point(460, 142);
            this.numKapasite.Name = "numKapasite";
            this.numKapasite.Size = new System.Drawing.Size(90, 22);
            this.numKapasite.TabIndex = 7;
            //
            // lblAciklama
            //
            this.lblAciklama.AutoSize = true;
            this.lblAciklama.Location = new System.Drawing.Point(360, 180);
            this.lblAciklama.Name = "lblAciklama";
            this.lblAciklama.Size = new System.Drawing.Size(62, 16);
            this.lblAciklama.TabIndex = 8;
            this.lblAciklama.Text = "Açıklama";
            //
            // txtAciklama
            //
            this.txtAciklama.Location = new System.Drawing.Point(360, 202);
            this.txtAciklama.Multiline = true;
            this.txtAciklama.Name = "txtAciklama";
            this.txtAciklama.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtAciklama.Size = new System.Drawing.Size(350, 140);
            this.txtAciklama.TabIndex = 9;
            //
            // btnKaydet
            //
            this.btnKaydet.Location = new System.Drawing.Point(360, 360);
            this.btnKaydet.Name = "btnKaydet";
            this.btnKaydet.Size = new System.Drawing.Size(170, 44);
            this.btnKaydet.TabIndex = 10;
            this.btnKaydet.Text = "Değişiklikleri Kaydet";
            this.btnKaydet.UseVisualStyleBackColor = true;
            this.btnKaydet.Click += new System.EventHandler(this.btnKaydet_Click);
            //
            // btnIptalEt
            //
            this.btnIptalEt.Location = new System.Drawing.Point(540, 360);
            this.btnIptalEt.Name = "btnIptalEt";
            this.btnIptalEt.Size = new System.Drawing.Size(170, 44);
            this.btnIptalEt.TabIndex = 11;
            this.btnIptalEt.Text = "Etkinliği İptal Et";
            this.btnIptalEt.UseVisualStyleBackColor = true;
            this.btnIptalEt.Click += new System.EventHandler(this.btnIptalEt_Click);
            //
            // EtkinlikYonetForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(724, 418);
            this.Controls.Add(this.btnIptalEt);
            this.Controls.Add(this.btnKaydet);
            this.Controls.Add(this.txtAciklama);
            this.Controls.Add(this.lblAciklama);
            this.Controls.Add(this.numKapasite);
            this.Controls.Add(this.lblKapasite);
            this.Controls.Add(this.dtpTarih);
            this.Controls.Add(this.lblTarih);
            this.Controls.Add(this.lblDoluluk);
            this.Controls.Add(this.lblSecili);
            this.Controls.Add(this.lstEtkinlikler);
            this.Controls.Add(this.lblEtkinlikler);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EtkinlikYonetForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Etkinlik Yönetimi";
            this.Load += new System.EventHandler(this.EtkinlikYonetForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numKapasite)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblEtkinlikler;
        private System.Windows.Forms.ListBox lstEtkinlikler;
        private System.Windows.Forms.Label lblSecili;
        private System.Windows.Forms.Label lblDoluluk;
        private System.Windows.Forms.Label lblTarih;
        private System.Windows.Forms.DateTimePicker dtpTarih;
        private System.Windows.Forms.Label lblKapasite;
        private System.Windows.Forms.NumericUpDown numKapasite;
        private System.Windows.Forms.Label lblAciklama;
        private System.Windows.Forms.TextBox txtAciklama;
        private System.Windows.Forms.Button btnKaydet;
        private System.Windows.Forms.Button btnIptalEt;
    }
}
