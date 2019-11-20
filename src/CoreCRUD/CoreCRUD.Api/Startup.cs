﻿using CoreCRUD.Api.Validators;
using Equinox.Application.AutoMapper;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Swashbuckle.AspNetCore.Swagger;
using System.IO;
using CoreCRUD.Domain.Repositories;
using CoreCRUD.Infrastructure.Repository;
using CoreCRUD.Infrastructure.DbContext;
using CoreCRUD.Application.Interfaces.Services;
using CoreCRUD.Repository;
using CoreCRUD.Application.Services;
using App.Metrics;
using App.Metrics.Health;
using System;
using Microsoft.AspNetCore.Mvc;

namespace CoreCRUD.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            var metrics = new MetricsBuilder()
                                            .Configuration.Configure(
                                                options =>
                                                {
                                                    options.AddServerTag();
                                                    options.AddEnvTag();
                                                    options.AddAppTag();
                                                })
                                            .OutputMetrics.AsPrometheusPlainText()
                                            .Build();

            services.AddMetrics(metrics);

            var health = AppMetricsHealth.CreateDefaultBuilder()//.Configuration.Configure(new HealthOptions() { Enabled = true, ApplicationName = "http://health.local.com", ReportingEnabled = true })
            .Report.ToMetrics(metrics)
            .HealthChecks.AddPingCheck("Google Ping", "google.com", TimeSpan.FromSeconds(10))
            .HealthChecks.AddHttpGetCheck("GitHub", new Uri("https://github.com"), TimeSpan.FromSeconds(10))
            .HealthChecks.RegisterFromAssembly(services)
            .BuildAndAddTo(services);

            services.AddMetricsReportingHostedService();
            services.AddHealthReportingHostedService();

            services.AddHealth(health);

            services.AddMetricsEndpoints();
            services.AddHealthEndpoints();
            services.AddMetricsTrackingMiddleware();            
            services.AddMvc().AddMetrics();

            services.AddMvc().AddFluentValidation(fv => fv.RegisterValidatorsFromAssemblyContaining<ProdutoViewModelValidator>());

            // Configuro o auto mapper
            var config = AutoMapperConfig.RegisterMappings();
            var mapper = config.CreateMapper();
            services.AddSingleton(mapper);

            // Configuro o Swagger
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info
                {
                    Version = "v1",
                    Title = "Core CRUD",
                    Description = "Api de cadastro de produto",
                    TermsOfService = "",
                    Contact = new Contact { Name = "Fabrício Veronez", Email = "fabricioveronez@gmail.com", Url = "" },
                    License = new License { Name = "Sem licença.", Url = "" }
                });

                // Configuro os comentários do swagger JSON e UI.
                var basePath = PlatformServices.Default.Application.ApplicationBasePath;
                var xmlPath = Path.Combine(basePath, "CoreCRUD.Api.xml");
                c.IncludeXmlComments(xmlPath);
            });

            // Registro os objetos que vou usar na aplicação
            services.AddSingleton(this.Configuration);
            services.AddTransient<IProdutoService, ProdutoService>();
            services.AddTransient<IProdutoRepository, ProdutoRepository>();

            services.AddScoped<IDbContext>(sp =>
            {
                return new MongoContext()
                {
                    ConnectionString = this.Configuration.GetSection("Mongo:ConnectionString").Get<string>(),
                    DataBase = this.Configuration.GetSection("Mongo:DataBase").Get<string>()
                };
            });


            //services.AddScoped((sp) =>
            //{
            //    return sp.GetRequiredService<MongoContext>();
            //});

            //services.AddScoped<IDbContext, MongoContext>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors(builder =>
            {
                builder.WithOrigins("*")
                    .AllowAnyHeader()
                    .WithMethods("*")
                    .AllowCredentials();
            });

            app.UseHealthAllEndpoints();
            app.UseMetricsAllEndpoints();

            app.UseMetricsAllMiddleware();
            app.UseHttpsRedirection();

            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.), specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Core CRUD v1");
            });

            app.UseMvc();
        }
    }
}
