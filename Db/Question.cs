﻿using log4net;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Exceptions;
using Telegram.Messaging.CallbackHandlers;
using Telegram.Messaging.Messaging;
using Telegram.Messaging.Types;
using Utilities.String.Json.Extentions;

namespace Telegram.Messaging.Db
{
	public class Question
	{
		readonly static ILog log = LogManager.GetLogger(typeof(Question));

		public int SurveyId { get; set; }
		public Survey Survey { get; set; }
		public int Id { get; set; }
		public FieldTypes FieldTypeId { get; set; }
		public FieldType FieldType { get; set; }
		public bool PickOnlyDefaultAnswers { get; set; }
		public bool IsCompleted { get; set; }
		public bool IsMandatory { get; set; }
		public bool ExpectsCommand { get; set; }
		public DateTime CreatedUtc { get; set; }
		public string QuestionText { get; set; }
		public int InternalId { get; set; }
		public string FollowUp { get; set; }
		public int MaxButtonsPerRow { get; set; }
		[StringLength(10)]
		public string FollowUpSeparator { get; set; }
		public string Answers
		{
			get { return _telegramAnswers.Count > 0 ? JsonConvert.SerializeObject(_telegramAnswers) : null; }
			set
			{
				_telegramAnswers = JsonConvertExt.DeserializeObject<List<TelegramAnswer>>(value) ?? _telegramAnswers;
				_telegramAnswers.ForEach(x => x.AnsweredQuestion = this);
			}
		}
		public string Constraints
		{
			get { return TelegramConstraints.Count > 0 ? JsonConvertExt.SerializeIgnoreAndPopulate(TelegramConstraints) : null; }
			set { TelegramConstraints = JsonConvertExt.DeserializeObject<List<TelegramConstraint>>(value) ?? TelegramConstraints; }
		}
		public string DefaultAnswers
		{
			get { return _defaultAnswersList.Count > 0 ? JsonConvertExt.SerializeIgnoreAndPopulate(_defaultAnswersList) : null; }
			set { _defaultAnswersList = JsonConvertExt.DeserializeObject<List<TelegramChoice>>(value) ?? _defaultAnswersList; }
		}

		Type _callbackHandler;
		/// <summary>
		/// This type will be instanciated and events called accordignly on the target.
		/// When used in a MessageManager, only one target object will be created, so information set in the target object will be persistent between calls.
		/// This property is not related to OnEvent, but target objects are the same
		/// </summary>
		[NotMapped]
		public Type CallbackHandler
		{
			get => _callbackHandler;
			set
			{
				if (value == null) { _callbackHandler = null; return; }
				if (value.IsSubclassOf(typeof(QuestionAnswerCallbackHandler)) == false)
					throw new Exception($"{value} is not a subclass of {typeof(QuestionAnswerCallbackHandler).Name}");
				_callbackHandler = value;
			}
		}

		/// <summary>
		/// A string representation of CallbackHandler
		/// </summary>
		public string CallbackHandlerAssemblyName
		{
			get
			{
				if (_callbackHandler == null) return null;
				var assmb = _callbackHandler.AssemblyQualifiedName;
				return assmb;
			}
			set
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					_callbackHandler = null;
					return;
				}
				Type t = Type.GetType(value, false, false);
				if (t == null || t.IsSubclassOf(typeof(QuestionAnswerCallbackHandler)) == false)
					throw new Exception($"Type {t} was not found or it is not a subclass of {typeof(QuestionAnswerCallbackHandler).Name}");
				_callbackHandler = t;
			}
		}

		Action<MessagingEventArgs> _onEvent;
		/// <summary>
		/// The action to call when there is an event associated to this question.
		/// The target object must be of type <see cref="IQuestionAnswerCallbackHandler"/>, otherwise the action should refer a static method
		/// When used in a MessageManager, only one target object will be created, so information set in the target object will be persistent between calls.
		/// This property is not related to CallbackHandler, but target objects are the same
		/// </summary>
		[NotMapped]
		public Action<MessagingEventArgs> OnEvent
		{
			get => _onEvent;
			set
			{
				if (value == null) { _onEvent = null; return; }
				if (value.Target != null && typeof(IQuestionAnswerCallbackHandler).IsAssignableFrom(value.Target.GetType()) == false)
					throw new InvalidParameterException($"OnEvent target must implement interface {nameof(IQuestionAnswerCallbackHandler)}");
				_onEvent = value;
			}
		}

		/// <summary>
		/// A string representation of OnEvent
		/// </summary>
		public string MethodNameOnEvent
		{
			get
			{
				if (_onEvent == null) return null;
				return _onEvent.Method.DeclaringType.AssemblyQualifiedName + "`" + _onEvent.Method.IsStatic + "`" + _onEvent.Method.Name;
			}
			set
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					_onEvent = null;
					return;
				}
				string[] typeAndMethod = value.Split(new char[] { '`' });
				if (typeAndMethod.Length != 3) return;

				Type t = Type.GetType(typeAndMethod[0], false, false);
				if (t == null) return;

				bool isStatic = bool.Parse(typeAndMethod[1]);

				string method = typeAndMethod[2];
				if (string.IsNullOrWhiteSpace(method)) return;

				try
				{
					if (isStatic)
					{
						_onEvent = (Action<MessagingEventArgs>)Action.CreateDelegate(typeof(Action<MessagingEventArgs>), t, method);
					}
					else
					{
						object target = Activator.CreateInstance(t);
						_onEvent = (Action<MessagingEventArgs>)Action.CreateDelegate(typeof(Action<MessagingEventArgs>), target, method);
					}
				}
				catch (Exception ex)
				{
					log.Debug($"Error creating delegate OnEvent. If target method changed from static to non-static, it is normal. {typeAndMethod}", ex);
				}
			}
		}

		List<TelegramChoice> _defaultAnswersList;
		[NotMapped]
		public TelegramChoice[] DefaultAnswersList
		{
			get => _defaultAnswersList.ToArray();
			//private set => _defaultAnswersList = value != null? : _defaultAnswersList ;
		}
		[NotMapped]
		public TelegramAnswer LastAnswer { get { return _telegramAnswers.Count == 0 ? null : _telegramAnswers[_telegramAnswers.Count - 1]; } }
		[NotMapped]
		public TelegramAnswer[] TelegramAnswers { get => _telegramAnswers.ToArray(); }
		List<TelegramAnswer> _telegramAnswers;
		[NotMapped]
		public List<TelegramConstraint> TelegramConstraints { get; set; }

		public Question()
		{
			CreatedUtc = DateTime.UtcNow;
			_telegramAnswers = new List<TelegramAnswer>();
			TelegramConstraints = new List<TelegramConstraint>();
			_defaultAnswersList = new List<TelegramChoice>();
			FieldTypeId = FieldTypes.None;// -1;
		}

		public Question(int internalId) : this()
		{
			InternalId = internalId;
		}

		public void DeriveConstraintFromFieldType()
		{
			if (FieldTypeId == FieldTypes.None) return;
			if (TelegramConstraints.Where(x => x.Type == FieldTypeId).Count() == 0)
				TelegramConstraints.Add(TelegramConstraint.FromTypeId((FieldTypes)FieldTypeId));
		}

		/// <summary>
		/// Adds an answer to the answers of this questions. 
		/// If the passed string is a json-serialized TelegramChoice, it will be treated as such.
		/// Constraints are enforced before adding it to the list and IsCompleted is set to the EnforceConstraints result
		/// </summary>
		/// <param name="answer">The simple answer or a json-serialized TelegramChoice</param>
		/// <returns></returns>
		public TelegramAnswer AddAnswer(string answer)
		{
			TelegramChoice choice = JsonConvertExt.DeserializeObject<TelegramChoice>(answer);
			TelegramAnswer telegramAnswer;
			if (choice != null)
				telegramAnswer = new TelegramAnswer(this, choice);
			else
				telegramAnswer = new TelegramAnswer(this, answer);
			_telegramAnswers.Add(telegramAnswer);
			IsCompleted = telegramAnswer.EnforceConstraints();
			return telegramAnswer;
		}

		/// <summary>
		/// Adds an answer to the answers of this questions. 
		/// Constraints are enforced before adding it to the list and IsCompleted is set to the EnforceConstraints result
		/// </summary>
		public TelegramAnswer AddAnswer(TelegramChoice choice)
		{
			TelegramAnswer telegramAnswer = new TelegramAnswer(this, choice);
			telegramAnswer.EnforceConstraints();
			_telegramAnswers.Add(telegramAnswer);
			IsCompleted = telegramAnswer.EnforceConstraints();
			return telegramAnswer;
		}

		/// <summary>
		/// Adds an answer to the answers of this questions. If the answer.Question != null or this questions contains already this answer, null will be returned.
		/// Constraints are enforced before adding it to the list and IsCompleted is set to the EnforceConstraints result
		/// </summary>
		public TelegramAnswer AddAnswer(TelegramAnswer answer)
		{
			if (answer == null || answer.AnsweredQuestion != this) return null;

			answer.EnforceConstraints();
			if (_telegramAnswers.Contains(answer)) return null;
			_telegramAnswers.Add(answer);
			IsCompleted = answer.EnforceConstraints();
			return answer;
		}

		/// <summary>
		/// Updates this Question to the DB
		/// </summary> 
		/// <returns></returns>
		public async Task UpdateQuestion()
		{
			using (var db = new MessagingDb())
			{
				try
				{
					db.Attach(this);
					var entry = db.Entry(this);
					if (entry.State != EntityState.Added)
						entry.State = EntityState.Modified;
					int k = await db.SaveChangesAsync();
				}
				catch (Exception ex)
				{
					log.Debug($"Error updating Question {this.QuestionText}", ex);
				}
			}
		}

		internal async Task Delete()
		{
			using (var db = new MessagingDb())
			{
				try
				{
					db.Entry(this).State = EntityState.Deleted;
					db.Questions.Remove(this);
					await db.SaveChangesAsync();
				}
				catch (Exception ex)
				{
					log.Debug($"Error deleting Question {this.QuestionText}", ex);
				}
			}
		}

		public override string ToString()
		{
			return $"{Id}|{InternalId}|{SurveyId}|{FieldTypeId}|{IsCompleted}|{IsMandatory}|{ExpectsCommand}|{QuestionText}|{FollowUp}";
		}

		public void AddDefaultAnswer(string value, string label = null)
		{
			if (string.IsNullOrWhiteSpace(value))
				return;
			if (label == null) label = value;
			AddDefaultAnswer(new TelegramChoice(label, value));
		}

		public void AddDefaultAnswers(string[] defAnswers)
		{
			if (defAnswers == null) return;
			foreach (string s in defAnswers)
				if (string.IsNullOrWhiteSpace(s))
					continue;
				else
					AddDefaultAnswer(new TelegramChoice(s, s));
		}

		/// <summary>
		/// Adds a list of default answers and checks that all of them respect the constraints
		/// </summary>
		/// <param name="defAnswers"></param>
		public void AddDefaultAnswers(List<TelegramChoice> defAnswers)
		{
			if (defAnswers == null) return;

			defAnswers.ForEach(answer =>
			{
				TelegramConstraints.ForEach(constraint =>
					{
						if (answer != TelegramChoice.NewKeyboardLine && answer.IsSystemChoice == false && constraint.Validate(answer.Value) == false)
							log.Debug($"Choice {answer.Value} is not valid for this question {QuestionText}, type:{FieldTypeId}");
					}
				);
				_defaultAnswersList.Add(answer);
			});
		}

		/// <summary>
		/// Adds a default answer and checks that it respects the constraints
		/// </summary>
		/// <param name="defAnswers"></param>
		public void AddDefaultAnswer(TelegramChoice answer)
		{
			if (answer == null) return;

			foreach (var constraint in TelegramConstraints)
				if (answer != TelegramChoice.NewKeyboardLine && answer.IsSystemChoice == false && constraint.Validate(answer.Value) == false)
				{
					//log.Debug($"Choice {answer.Value} is not valid for this question {QuestionText}, type:{FieldTypeId}");
					return;
				}
			_defaultAnswersList.Add(answer);
		}

		public void UpdateDefaultAnswerLabel(string value, string label)
		{
			var da = _defaultAnswersList.Where(x => x.Value == value).FirstOrDefault();
			if (da == null) return;
			da.Label = label;
		}

		public void RemoveDefaultAnswer(TelegramChoice answer)
		{
			_defaultAnswersList.Remove(answer);
		}

		public void RemoveDefaultAnswer(string havingValue)
		{
			_defaultAnswersList.RemoveAll(x => x.Value == havingValue);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="keepSystemChoices">True whether to keep back,cancel,skip and all the other system buttons specified when adding the question</param>
		public void ClearDefaultAnswersList(bool keepSystemChoices = false)
		{
			if (keepSystemChoices == false)
				_defaultAnswersList.Clear();
			else
				_defaultAnswersList.RemoveAll(x => x.IsSystemChoice == false);
		}

		/// <summary>
		/// Get the latest question asked to the user, even if the question expired
		/// </summary>
		/// <param name="tid"></param>
		/// <returns></returns>
		public static async Task<Question> GetMostRecentAsync(int tid)
		{
			try
			{
				using (var db = new MessagingDb())
				{
					return await (from qs in db.Questions.Include(x => x.Survey)
								  where qs.Survey.TelegramUserId == tid
								  select qs).OrderByDescending(x => x.Id).FirstOrDefaultAsync().ConfigureAwait(false);
				}
			}
			catch (Exception e)
			{
				log.Error($"error getting the most recent question for {tid}", e);
				return null;
			}
		}

		/// <summary>
		/// Get the latest question asked to the user, even if the question expired
		/// </summary>
		/// <param name="tid"></param>
		/// <returns></returns>
		public static Question GetMostRecent(int tid)
		{
			try
			{
				using (var db = new MessagingDb())
				{
					return (from qs in db.Questions.Include(x => x.Survey)
							where qs.Survey.TelegramUserId == tid
							select qs).OrderByDescending(x => x.Id).FirstOrDefault();
				}
			}
			catch (Exception e)
			{
				log.Error($"error getting the most recent question for {tid}", e);
				return null;
			}
		}
	}
}
