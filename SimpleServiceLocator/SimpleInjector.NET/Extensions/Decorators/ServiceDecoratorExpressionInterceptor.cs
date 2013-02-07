﻿#region Copyright (c) 2010 S. van Deursen
/* The Simple Injector is an easy-to-use Inversion of Control library for .NET
 * 
 * Copyright (C) 2010 S. van Deursen
 * 
 * To contact me, please visit my blog at http://www.cuttingedge.it/blogs/steven/ or mail to steven at 
 * cuttingedge.it.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
 * associated documentation files (the "Software"), to deal in the Software without restriction, including 
 * without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
 * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the 
 * following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial 
 * portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO 
 * EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER 
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE 
 * USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion

namespace SimpleInjector.Extensions.Decorators
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Threading;
    using SimpleInjector.Lifestyles;

    internal sealed class ServiceDecoratorExpressionInterceptor : DecoratorExpressionInterceptor
    {
        private static readonly object ContainerItemsKeyAndLock = new object();

        // Cache for decorators when the decorator is registered as singleton. Since all decoration requests
        // for the registration of that decorator will go through the same instance, we can (or must)
        // define this dictionary as instance field (not as static or thread-static). When a decorator is
        // registered 
        private readonly Dictionary<Type, Registration> registrations = new Dictionary<Type, Registration>();

        internal ServiceDecoratorExpressionInterceptor(DecoratorExpressionInterceptorData data)
            : base(data)
        {
        }

        protected override Dictionary<Type, ServiceTypeDecoratorInfo> ThreadStaticServiceTypePredicateCache
        {
            get { return this.GetThreadStaticServiceTypePredicateCacheByKey(ContainerItemsKeyAndLock); }
        }

        internal void Decorate(object sender, ExpressionBuiltEventArgs e)
        {
            Type decoratorType;

            if (this.MustDecorate(e.RegisteredServiceType, out decoratorType) && this.SatisfiesPredicate(e))
            {
                this.ApplyDecorator(decoratorType, e);
            }
        }

        private void ApplyDecorator(Type decoratorType, ExpressionBuiltEventArgs e)
        {
            var constructor = this.ResolutionBehavior.GetConstructor(e.RegisteredServiceType, decoratorType);

            e.Expression = this.BuildDecoratorExpression(constructor, e);

            e.Lifestyle = this.Lifestyle;

            this.AddKnownDecoratorRelationships(constructor, e);

            this.AddAppliedDecorator(decoratorType, e);
        }

        private void AddKnownDecoratorRelationships(ConstructorInfo constructor, ExpressionBuiltEventArgs e)
        {
            var info = this.GetServiceTypeInfo(e);

            var decoratee = info.GetCurrentInstanceProducer();

            // Must be called before the current decorator is added to the list of applied decorators
            var relationships =
                this.GetKnownDecoratorRelationships(constructor, e.RegisteredServiceType, decoratee);

            e.KnownRelationships.AddRange(relationships);
        }

        private void AddAppliedDecorator(Type decoratorType, ExpressionBuiltEventArgs e)
        {
            var info = this.GetServiceTypeInfo(e);

            // Add the decorator to the list of applied decorators. This way users can use this information in 
            // the predicate of the next decorator they add.
            info.AddAppliedDecorator(decoratorType, info.ImplementationType, this, e.Expression);
        }

        private Expression BuildDecoratorExpression(ConstructorInfo decoratorConstructor,
            ExpressionBuiltEventArgs e)
        {
            var decorateeParameter = (
                from parameter in decoratorConstructor.GetParameters()
                where IsDecorateeParameter(parameter, e.RegisteredServiceType)
                select parameter)
                .Single();
            
            var decorateeExpression = GetExpressionForDecorateeDependencyParameterOrNull(decorateeParameter, 
                e.RegisteredServiceType, e.Expression);

            var registration = this.CreateRegistrationFromCache(e, decoratorConstructor);

            // By creating the decorator using a Lifestyle Registration the decorator can be completely
            // incorperated into the pipeline. This means that the ExpressionBuilding can be applied and it
            // can be wrapped with an initializer.
            return registration.BuildExpression();
        }

        private Registration CreateRegistrationFromCache(ExpressionBuiltEventArgs e, 
            ConstructorInfo decoratorConstructor)
        {
            Registration registration;

            // Ensure that the registration for the decorator is created only once to prevent the possibility
            // of multiple instances being created when dealing lifestyles that cache an instance within the
            // Registration instance itself (such as the Singleton lifestyle does).
            lock (this.registrations)
            {
                if (!this.registrations.TryGetValue(e.RegisteredServiceType, out registration))
                {
                    registration = 
                        this.CreateRegistration(e.RegisteredServiceType, decoratorConstructor, e.Expression);

                    this.registrations[e.RegisteredServiceType] = registration;
                }
            }

            return registration;
        }
    }
}